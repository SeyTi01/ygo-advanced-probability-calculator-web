using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Search;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Compilation;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Counting;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.DrawResolution;

internal sealed partial class PostDrawCalculation {
    private StreamedSearchState? _streamedSearch;
    private sealed record StreamedSearchState(
        List<Card> Cards,
        OrderedDrawStateLayout Layout,
        OrderedResolver Resolver,
        BigInteger[] Factorial,
        int Source,
        BigInteger[][] Primary,
        BigInteger[][] Witness
    );

    private bool CanStreamSearch(
        List<Card> cards,
        Dictionary<string, List<Card>> identities,
        List<Combo> combos,
        OrderedResolver resolver,
        int copies,
        out int source
    ) {
        source = resolver.SingleSource(handSize, copies);
        budget.Spend(cards.Count);
        if (source < 0 || cards.Any(card => card.EffectPlan.RequiresSearchResolution
            || (card.Search is not null && card.MinimumHandSize > 0))) {
            return false;
        }
        // Existing classes already preserve direct IDs, search and removal
        // masks. Check full predicate truth as well: numeric/objective selectors
        // need not be determined by their category atoms alone.
        foreach (Combo combo in combos) {
            foreach (ComboCategory requirement in combo.AllCategories) {
                foreach (Card card in cards) {
                    budget.Spend((requirement.Selector.EvaluationSize + 1L) * (card.Categories.Count + 1L));
                    bool expected = requirement.Selector.Matches(card);
                    foreach (Card original in identities[card.Id]) {
                        budget.Spend((requirement.Selector.EvaluationSize + 1L) * (original.Categories.Count + 1L));
                        if (requirement.Selector.Matches(original) != expected) {
                            return false;
                        }
                    }
                }
            }
        }
        return true;
    }

    // The single-source quotient preserves the complete held state at the
    // unchanged policy boundary. Fast observes that hand and scalar capacity,
    // never hidden survivors. All original targets in a class have identical
    // final/search predicates: sum(n_i-h_i)=n_C-h_C, so the old conditional
    // identity expansion has exactly the same Boolean access event.
    //
    // Compile that SAME optimistic projection once, then follow its decision
    // edges for each observed hand. The empty-assignment event is aggregated
    // alongside it, in the same traversal and denominator. Only a completed
    // primary publishes these small sums; optional stronger proofs run later.
    private ExactCalculationResult CountStreamedSearch(
        StreamedSearchState state,
        List<Combo> combos,
        IReadOnlyList<ComboGroup>? groups,
        bool totalOnly
    ) {
        StreamedSearchPredicates counter = new(state.Cards, combos, budget, includeWitness: true);
        using IDisposable? measurement = budget.SearchDiagnostics?.Measure("streamed search");
        bool countAbsent = ObserveTerminal is null;
        if (countAbsent) {
            try {
                counter.CountAbsent(state.Source, handSize, state.Factorial, state.Primary[0], state.Witness[0], groups);
            }
            catch (ProbabilityCalculationLimitException exception) when (exception.Reason == ProbabilityCalculationLimitReason.Storage) {
                // Counting a union may need more decision nodes than evaluating
                // each row. Discard every partial sum and use the existing stream;
                // all work already performed remains charged to this request.
                budget.Spend(state.Primary[0].Length + state.Witness[0].Length);
                Array.Clear(state.Primary[0]);
                Array.Clear(state.Witness[0]);
                countAbsent = false;
                budget.SearchDiagnostics?.Add("streamed absent storage fallback");
            }
        }
        state.Resolver.TryEnumerateSingleSource(handSize, state.Factorial.Length - 1, state.Factorial, (values, remaining, weight) => {
            budget.SearchDiagnostics?.Add("streamed search terminals");
            ReadOnlySpan<int> held = values.AsSpan(state.Layout.HandStart, state.Cards.Count);
            int hidden = state.Layout.HasHiddenCount ? values[state.Layout.HiddenCount] : 0;
            bool[] success = counter.Evaluate(held, remaining - hidden, 0);
            BigInteger mass = StreamedMass(state, remaining, weight);
            // Observing at least one source causes the single permitted
            // activation. Unused copies may remain held or unseen afterward;
            // their count cannot identify this partition by a zero test.
            int partition = values[state.Source] < state.Cards[state.Source].Copies ? 1 : 0;
            RecordStreamed(state.Primary[partition], success, mass, combos, groups);
            RecordStreamed(state.Witness[partition], counter.Witness, mass, combos, groups);
            ObserveTerminal?.Invoke(SearchTerminal.Create(values, state.Layout, remaining), mass);
        }, sourcePresent: countAbsent ? true : null);
        return StreamedResult(state, state.Primary[0], state.Primary[1], combos.Count, totalOnly);
    }

    private ExactCalculationResult CountStreamedLower(
        StreamedSearchState state,
        List<Combo> combos,
        IReadOnlyList<ComboGroup>? groups,
        bool declineSearch,
        bool useKnownLoss
    ) {
        if (declineSearch) {
            return StreamedResult(state, state.Witness[0], state.Witness[1], combos.Count, false);
        }
        Card source = state.Cards[state.Source];
        // A mandatory discard larger than the available hand has no successful
        // source-present terminal. Clamp only this unreachable loss-table index.
        int lost = (int)Math.Min(state.Factorial.Length - 1L,
            1L + source.PostDrawDiscardCount + source.PreDrawDeckRemovalCount + source.PostDrawDeckMillCount);
        int loss = useKnownLoss ? OrderedSearchLossBudget.Create(state.Cards, budget)[lost] : lost;
        // Source absence has no loss: its completed primary already IS the
        // conservative event. For source presence, if every target capacity is
        // zero, the stronger pass IS the completed empty-assignment witness.
        // This tests the existing lower proof, not actual hidden survival.
        bool anyTarget = false;
        foreach (Card searcher in state.Cards) {
            budget.Spend(1);
            if (searcher.Search is null) {
                continue;
            }
            foreach (Card target in state.Cards) {
                budget.Spend((searcher.Search.Selector.EvaluationSize + 1L) * (target.Categories.Count + 1L));
                anyTarget |= target.Copies > loss && searcher.Id != target.Id && searcher.Search.Matches(target);
            }
        }
        if (!anyTarget) {
            return StreamedResult(state, state.Primary[0], state.Witness[1], combos.Count, false);
        }

        BigInteger[] wins = new BigInteger[state.Primary[0].Length];
        CheckStorage(wins.Length, wins.Length * (WorkBudget.IntegerCells(state.Factorial[^1]) + 1), "streamed lower results");
        StreamedSearchPredicates counter = new(state.Cards, combos, budget, includeWitness: false);
        using IDisposable? measurement = budget.SearchDiagnostics?.Measure("streamed lower search");
        state.Resolver.TryEnumerateSingleSource(handSize, state.Factorial.Length - 1, state.Factorial, (values, remaining, weight) => {
            budget.SearchDiagnostics?.Add("streamed lower terminals");
            ReadOnlySpan<int> held = values.AsSpan(state.Layout.HandStart, state.Cards.Count);
            int hidden = state.Layout.HasHiddenCount ? values[state.Layout.HiddenCount] : 0;
            bool[] success = counter.Evaluate(held, remaining - hidden, loss);
            RecordStreamed(wins, success, StreamedMass(state, remaining, weight), combos, groups);
        }, sourcePresent: true);
        return StreamedResult(state, state.Primary[0], wins, combos.Count, false);
    }

    private BigInteger StreamedMass(StreamedSearchState state, int remaining, BigInteger weight) {
        budget.Spend((WorkBudget.IntegerCells(weight) + 1) * (WorkBudget.IntegerCells(state.Factorial[remaining]) + 1));
        // Hidden positions remain among the exchangeable unseen labels. Fast
        // never allocates their identities: all remaining! suffixes count once.
        return weight * state.Factorial[remaining];
    }

    private void RecordStreamed(
        BigInteger[] wins,
        bool[] success,
        BigInteger mass,
        List<Combo> combos,
        IReadOnlyList<ComboGroup>? groups
    ) {
        long cells = WorkBudget.IntegerCells(mass);
        budget.Spend(combos.Count + cells);
        if (success.Any(value => value)) {
            wins[0] += mass;
        }
        for (int combo = 0; combo < combos.Count; combo++) {
            budget.Spend(cells + 1);
            if (success[combo]) {
                wins[1 + combo] += mass;
            }
        }
        for (int group = 0; group < (groups?.Count ?? 0); group++) {
            budget.Spend(combos.Count + cells);
            for (int combo = 0; combo < combos.Count; combo++) {
                if (success[combo] && combos[combo].GroupId == groups![group].Id) {
                    wins[1 + combos.Count + group] += mass;
                    break;
                }
            }
        }
    }

    private ExactCalculationResult StreamedResult(
        StreamedSearchState state,
        BigInteger[] first,
        BigInteger[] second,
        int combos,
        bool totalOnly
    ) {
        budget.Spend(first.Length * (WorkBudget.IntegerCells(state.Factorial[^1]) + 1));
        ExactProbability[] results = first.Select((win, row) => new ExactProbability(win + second[row], state.Factorial[^1])).ToArray();
        return new(results[0], totalOnly ? [] : results.Skip(1).Take(combos).ToArray(),
            totalOnly ? [] : results.Skip(1 + combos).ToArray());
    }

    private sealed class StreamedSearchPredicates {
        private readonly List<Card> _cards;
        private readonly List<Combo> _combos;
        private readonly WorkBudget _budget;
        private readonly SearchAccessPlan _search;
        private readonly int[] _rows;
        private readonly int[] _hand;
        private readonly bool[] _success;
        private readonly bool _includeWitness;
        private SearchDecisionCounter? _counter;
        private int[] _roots = [];
        private int[] _witnessRoots = [];
        private (int Hand, int Capacity, int Loss) _key;
        internal bool[] Witness { get; }

        internal StreamedSearchPredicates(List<Card> cards, List<Combo> combos, WorkBudget budget, bool includeWitness) {
            budget.Spend(cards.Count * (long)(cards.Count + 1));
            budget.CheckStorage(cards.Count + combos.Count, 4L * cards.Count + 4L * combos.Count);
            _rows = Enumerable.Range(0, cards.Count).OrderByDescending(row => cards[row].Search is not null).ToArray();
            _cards = _rows.Select(row => cards[row]).ToList();
            _combos = combos;
            _budget = budget;
            _search = new(_cards, budget);
            _hand = new int[cards.Count];
            _success = new bool[combos.Count];
            _includeWitness = includeWitness;
            Witness = includeWitness ? new bool[combos.Count] : [];
        }

        internal bool[] Evaluate(ReadOnlySpan<int> held, int capacity, int loss) {
            _budget.Spend(held.Length + _combos.Count + 1L);
            int size = 0;
            for (int row = 0; row < _rows.Length; row++) {
                _hand[row] = held[_rows[row]];
                size += _hand[row];
            }
            int limit = Math.Min(size, Math.Max(0, capacity));
            (int Hand, int Capacity, int Loss) key = (size, limit, limit == 0 ? 0 : loss);
            if (_counter is null || key != _key) {
                Compile(size, limit, key.Loss);
                _key = key;
            }
            for (int combo = 0; combo < _success.Length; combo++) {
                _success[combo] = _counter.Evaluate(_roots[combo], _hand);
                if (_includeWitness) {
                    Witness[combo] = _counter.Evaluate(_witnessRoots[combo], _hand);
                }
            }
            return _success;
        }

        // With no source in the opening hand, there are no draws or removals.
        // Compile against the original deck: a source excluded from this random
        // hand can still be fetched by a search. Intersect each event with zero
        // held sources. Each counted h-subset has h!(N-h)! permutations,
        // so primary, witness and every existential union use the same N! mass.
        internal void CountAbsent(
            int source,
            int size,
            BigInteger[] factorial,
            BigInteger[] primary,
            BigInteger[] witness,
            IReadOnlyList<ComboGroup>? groups
        ) {
            int remaining = factorial.Length - 1 - size;
            BigInteger mass = BigInteger.Zero;
            int absent = 0;
            bool completed = false;
            try {
                int capacity = Math.Min(size, remaining);
                Compile(size, capacity, 0);
                _key = (size, capacity, 0);
                _budget.Spend(_rows.Length);
                int row = Array.IndexOf(_rows, source);
                absent = _counter!.Union([new CompiledEvent([new(BigInteger.One << row, 0, 0)])]);
                _budget.Spend((WorkBudget.IntegerCells(factorial[size]) + 1)
                    * (WorkBudget.IntegerCells(factorial[remaining]) + 1));
                mass = factorial[size] * factorial[remaining];
                CountRows(_roots, primary);
                CountRows(_witnessRoots, witness);
                _budget.SearchDiagnostics?.Add("streamed absent counts");
                completed = true;
            }
            finally {
                // Roots still describe the original full domains and can serve
                // the activated hand too. Release incomplete graphs before the
                // storage fallback retries the original streaming operation.
                if (!completed) {
                    _counter = null;
                    _roots = [];
                    _witnessRoots = [];
                }
            }

            void CountRows(int[] roots, BigInteger[] wins) {
                int total = 0;
                for (int combo = 0; combo < roots.Length; combo++) {
                    total = _counter!.Or(total, roots[combo]);
                    wins[1 + combo] = Count(roots[combo]);
                    _budget.SearchDiagnostics?.Add("streamed absent counted rows");
                }
                wins[0] = Count(total);
                for (int group = 0; group < (groups?.Count ?? 0); group++) {
                    int root = 0;
                    for (int combo = 0; combo < roots.Length; combo++) {
                        _budget.Spend(1);
                        if (_combos[combo].GroupId == groups![group].Id) {
                            root = _counter!.Or(root, roots[combo]);
                        }
                    }
                    wins[1 + roots.Length + group] = Count(root);
                }
            }

            BigInteger Count(int root) {
                BigInteger subsets = _counter!.Count(_counter.And(root, absent));
                _budget.Spend((WorkBudget.IntegerCells(subsets) + 1) * (WorkBudget.IntegerCells(mass) + 1));
                return subsets * mass;
            }
        }

        private void Compile(int size, int capacity, int loss) {
            // A single bounded template, never a map indexed by terminal hands.
            _counter = null;
            _roots = [];
            _witnessRoots = [];
            _budget.SearchDiagnostics?.Add("streamed predicate compilations");
            List<List<CompiledEvent?>> events = _search.Compile(_combos, size, _cards, capacity, loss);
            SearchDecisionCounter counter = new(_cards, size, _budget);
            _roots = events.Select(counter.Union).ToArray();
            if (_includeWitness) {
                ComboEventCompiler compiler = new(_cards, size, _budget);
                _witnessRoots = _combos.Select(combo => counter.Union(compiler.CompileCombo(combo))).ToArray();
            }
            _counter = counter;
        }
    }
}
