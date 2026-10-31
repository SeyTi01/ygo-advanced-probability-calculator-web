using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Search;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Certification;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Compilation;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Counting;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.DrawResolution;

internal sealed partial class PostDrawCalculation {
    // These references keep the already-budgeted terminal map alive only until
    // this request's optional certificate completes; no second ordered frontier.
    private SearchCertificateState? _searchCertificate;
    private sealed record SearchCertificateState(
        List<Card> Cards,
        List<Card> Deferred,
        Dictionary<string, List<Card>> Identities,
        Dictionary<State, BigInteger> Terminal,
        bool Packed,
        BigInteger Denominator
    );

    private interface ITerminalHand {
        static abstract int[] Create(int[] state, OrderedDrawStateLayout layout, int remaining);
    }

    private readonly struct HandTerminal : ITerminalHand {
        public static int[] Create(int[] state, OrderedDrawStateLayout layout, int remaining) =>
            state.AsSpan(layout.HandStart, layout.RowCount).ToArray();
    }

    private readonly struct SearchTerminal : ITerminalHand {
        public static int[] Create(int[] state, OrderedDrawStateLayout layout, int remaining) {
            int[] hand = new int[layout.RowCount + 1];
            state.AsSpan(layout.HandStart, layout.RowCount).CopyTo(hand);
            hand[^1] = Math.Max(0, remaining - (layout.HasHiddenCount ? state[layout.HiddenCount] : 0));

            return hand;
        }
    }

    internal ExactCalculationResult CalculateSearch(
        List<Combo> combos,
        IReadOnlyList<ComboGroup>? groups,
        bool totalOnly,
        SearchAccessPlan search,
        bool physical = false,
        PhysicalEnumerationMethod method = PhysicalEnumerationMethod.Auto,
        bool coalesceOrdinary = true
    ) {
        _searchCertificate = null;
        _streamedSearch = null;
        List<Card> cards = ReduceSearchDeck(combos, out Dictionary<string, List<Card>> identities, merge: !physical && coalesceOrdinary);
        // Only removable searchers need policy target counts. Unrelated targets
        // can still join the deferred pool when no removal can observe them.
        bool compactPhysical = physical && method == PhysicalEnumerationMethod.Auto && deferIdentities
            && cards.All(card => !card.EffectPlan.RequiresSearchResolution);
        // The same whole-trace exchangeability proof applies to optimistic and
        // lower counting: expand original source identities only in the final
        // conditional pool. Bounded hand operations and activation restrictions
        // retain their existing partition; removal material and observed targets
        // remain explicit in DeferUnobservedIdentities.
        bool compactFast = !physical && deferSearchers && cards.All(card => !card.EffectPlan.RequiresSearchResolution);
        HashSet<string> observed = SearchPolicyObservedRows(cards, deferUnremovableSearchers: compactPhysical || compactFast);
        List<Card> deferred = (!physical || compactPhysical) && deferIdentities ? DeferUnobservedIdentities(ref cards, observed) : [];
        compactPhysical &= deferred.Count > 0;

        if (physical && !compactPhysical && coalesceOrdinary) {
            budget.SearchDiagnostics?.Add("physical input rows", cards.Count);
            cards = CoalescePhysicalSearchDeck(cards, combos);
            budget.SearchDiagnostics?.Add("physical quotient rows", cards.Count);
        }

        Route = deferred.Count > 0 ? SolverRoute.SearchOrderedDeferred : SolverRoute.SearchOrdered;
        bool boundedSearch = cards.Any(card => card.EffectPlan.RequiresSearchResolution);
        // Only tied source ranks can observe encounter order. A complete custom
        // order (or a single search entry) keeps the existing count/batch quotient.
        int[] searchRanks = boundedSearch ? cards.Where(card => card.Search is not null)
            .Select(card => priority?.Rank(card.Id) ?? int.MaxValue).ToArray() : [];
        bool trackSearchOrder = searchRanks.Distinct().Count() < searchRanks.Length;
        OrderedDrawPreparation preparation = PrepareOrderedDraw(cards, combos, trackSearchOrder);
        int n = cards.Count;
        int copies = preparation.Copies;
        OrderedDrawStateLayout layout = preparation.Layout;
        bool[] relevant = preparation.Relevant;
        BigInteger[] factorial = preparation.Factorial;

        (bool[]?[] removalEligibility, bool[]?[]? costEligibility) = CompileHandOperations(cards);

        OrderedResolver resolver = new(
            this,
            budget,
            cards,
            layout,
            preparation.CustomPriority,
            preparation.Ranks,
            relevant,
            removalEligibility,
            costEligibility,
            new SearchKeepPolicy(cards, relevant, budget)
        );

        if (physical) {
            if (compactPhysical) {
                return CountSelectiveSurvivors(cards, deferred, resolver, factorial, combos, groups, totalOnly, search);
            }

            return CountPhysicalSearch(cards, layout, resolver, factorial, combos, groups, totalOnly, method);
        }

        if (StreamSearchTerminals && deferred.Count == 0
            && CanStreamSearch(cards, identities, combos, resolver, copies, out int source)) {
            int rows = new CalculationResultRows(combos, groups).Count;
            CheckStorage(4L * rows, 4L * rows * (WorkBudget.IntegerCells(factorial[copies]) + 1), "streamed search results");
            StreamedSearchState state = new(
                cards,
                layout,
                resolver,
                factorial,
                source,
                [new BigInteger[rows], new BigInteger[rows]],
                [new BigInteger[rows], new BigInteger[rows]]
            );

            ExactCalculationResult streamedResult = CountStreamedSearch(state, combos, groups, totalOnly);
            _streamedSearch = state;

            return streamedResult;
        }

        // Every held count and scalar capacity is bounded by copies. The length
        // word preserves every row, including trailing zeros. Use the encoding
        // only when it actually retains fewer cells; weights remain unchanged.
        bool packed = compactSearchTerminals && copies <= byte.MaxValue && 1 + (n + 4) / 4 < n + 1;
        Dictionary<State, BigInteger> terminal;
        using (budget.SearchDiagnostics?.Measure("draw distribution")) {
            terminal = resolver.Resolve<SearchTerminal>(handSize, copies, factorial, compactFrontierThreshold, packed);
        }

        budget.SearchDiagnostics?.Add("terminals", terminal.Count);
        budget.SearchDiagnostics?.Add("reduced rows", n);
        budget.SearchDiagnostics?.Add("deferred classes", deferred.Count);
        TerminalStates = terminal.Count;

        List<List<Card>> pools = [.. cards.Select(card => identities[card.Id])];

        if (deferred.Count > 0) {
            pools[^1] = [.. deferred.SelectMany(card => identities[card.Id])];
        }

        SearchEventTemplate? template = CreateSearchTemplate(pools, combos, search);
        ExactCalculationResult result = CountSearchHands(cards, pools, terminal, factorial[copies], combos, groups, totalOnly, search, template: template, packed: packed);
        _searchCertificate = new(cards, deferred, identities, terminal, packed, factorial[copies]);

        return result;
    }

    private List<Card> CoalescePhysicalSearchDeck(List<Card> cards, List<Combo> combos) {
        int count = cards.Count;
        CheckStorage(count, 3L * count, "physical row partition");
        bool[] joinsPrevious = new bool[count];

        for (int row = 1; row < count; row++) {
            budget.Spend(1);
            joinsPrevious[row] = Ordinary(cards[row - 1]) && Ordinary(cards[row]);
        }

        // Begin with the established canonical order. Category atoms and direct
        // IDs retain its group boundaries; complete selector truth also protects
        // numeric/objective predicates whose atoms alone are insufficient.
        foreach (Combo combo in combos) {
            foreach (ComboCategory requirement in combo.AllCategories) {
                foreach (CategoryBase category in requirement.CategoryBases) {
                    for (int row = 1; row < count; row++) {
                        budget.Spend(cards[row - 1].Categories.Count + (long)cards[row].Categories.Count + 1);
                        joinsPrevious[row] &= cards[row - 1].Categories.Contains(category) == cards[row].Categories.Contains(category);
                    }
                }

                Include(requirement.Selector);
            }

            foreach (ComboCard requirement in combo.AllCards) {
                for (int row = 1; row < count; row++) {
                    budget.Spend(1);
                    joinsPrevious[row] &= (cards[row - 1].Id == requirement.CardId) == (cards[row].Id == requirement.CardId);
                }
            }
        }

        foreach (Card source in cards) {
            if (source.Copies <= 0) {
                continue;
            }

            // Bounded searches can pay or remove hand material after draws.
            if (source.PreDrawRemovalCount > 0) {
                Include(source.PreDrawSelector);
            }

            if (source.PostDrawDiscardCount > 0) {
                Include(source.PostDrawSelector);
            }

            if (source.Search is not { } search) {
                continue;
            }

            foreach (SearchEffectMode mode in search.Modes ?? [new(search.Selector)]) {
                CheckStorage(count, 3L * count + mode.Selector.EvaluationSize, "physical row partition");
                bool[] eligible = new bool[count];

                for (int row = 0; row < count; row++) {
                    budget.Spend((mode.Selector.EvaluationSize + 1L) * (cards[row].Categories.Count + 1L));
                    eligible[row] = cards[row].Id != source.Id && search.Matches(cards[row], mode.Selector);
                }

                Refine(eligible);
            }
        }

        List<Card> reduced = new(count);
        long retainedCategoryCells = 0;

        for (int row = 0; row < count;) {
            budget.Spend(1);
            Card representative = cards[row];
            int copies = representative.Copies;
            int next = row + 1;

            while (next < count && joinsPrevious[next]) {
                budget.Spend(1);
                copies = checked(copies + cards[next].Copies);
                next++;
            }

            if (next > row + 1) {
                // WithCopies owns a category-list copy and checks every manual
                // key against it. Clone once per run; charge the full validation
                // bound and retain at most two int-sized cells per reference.
                int categories = representative.Categories.Count;
                retainedCategoryCells += 2L * categories;
                CheckStorage(count, 3L * count + retainedCategoryCells, "physical row partition");
                budget.Spend((categories + 1L) * (representative.ManualMetadataCategoryKeys.Count + 1L));
                representative = representative.WithCopies(copies);
            }

            reduced.Add(representative);
            row = next;
        }

        // Keep actual pooled remaining and held counts through draws and search.
        // Every policy transition observes identical selector truth within each
        // adjacent run; its internal tie winner changes only the pooled count.
        // Draw multiplicities add to the pool's remaining copies. Joint hidden
        // allocations sum to C(sum remaining, lost) by Vandermonde. Every source
        // and mode reaches either the entire pool or none of it, so shared physical
        // target capacity is its actual survivor count. No survivor identity is inferred.
        return reduced;

        static bool Ordinary(Card card) => card.Active && card.Copies > 0 && card.DrawCount is null && card.Search is null;

        void Include(CardSelector selector) {
            CheckStorage(count, 3L * count + selector.EvaluationSize, "physical row partition");
            Refine(CardSelectorCompiler.Compile(selector, cards, budget));
        }

        void Refine(bool[] eligible) {
            for (int row = 1; row < count; row++) {
                budget.Spend(1);
                joinsPrevious[row] &= eligible[row - 1] == eligible[row];
            }
        }
    }

    private List<Card> ReduceSearchDeck(List<Combo> combos, out Dictionary<string, List<Card>> identities, bool merge = true) {
        ReductionSelectors selectors = PrepareReductionSelectors(combos);
        List<bool[]> filters = selectors.Filters;

        // Equal combo/removal signatures alone do not imply search equivalence.
        // Keep target membership for every original source in each ordinary run.
        foreach (Card source in deck.Where(card => card.Copies > 0 && card.Search is not null)) {
            CheckStorage(filters.Count + 1L, (filters.Count + 1L) * deck.Count, "search reduction selectors");
            budget.Spend(deck.Sum(card => card.Categories.Count + 1L) * (source.Search!.Selector.EvaluationSize + 1L));
            filters.Add(deck.Select(card => card.Id != source.Id && source.Search!.Matches(card)).ToArray());
        }

        List<(Card Card, string Signature, string Eligibility)> entries = CreateReductionEntries(selectors);
        List<Card> reduced = [];
        identities = new(StringComparer.Ordinal);

        foreach (IGrouping<string, (Card Card, string Signature, string Eligibility)> group in entries
            .GroupBy(entry => entry.Signature).OrderBy(group => group.Key, StringComparer.Ordinal)) {
            // Retain the established membership/ordinary/ID tie order. Only merge
            // adjacent ordinary runs with equal eligibility for every effect.
            // Combining interleaved IDs across different filters changes removals.
            string? previousEligibility = null;

            foreach ((Card card, string _, string eligibility) in group.Where(entry => entry.Card.DrawCount is null)
                .OrderBy(entry => entry.Card.Id, StringComparer.Ordinal)) {

                if (merge && card.Search is null && reduced.Count > 0 && reduced[^1].Search is null && eligibility == previousEligibility) {
                    identities[reduced[^1].Id].Add(card);
                    reduced[^1] = reduced[^1].WithCopies(checked(reduced[^1].Copies + card.Copies));
                }
                else {
                    reduced.Add(card);
                    identities.Add(card.Id, [card]);
                    previousEligibility = eligibility;
                }
            }

            // Effect entries stay separate, preserving OPT identity AND custom rank.
            foreach (Card card in group.Where(entry => entry.Card.DrawCount is not null).Select(entry => entry.Card)
                .OrderBy(card => card.Id, StringComparer.Ordinal)) {
                reduced.Add(card);
                identities.Add(card.Id, [card]);
            }
        }

        return reduced;
    }

    // Coalesced ordinary labels share every combo, search and removal mask;
    // their individual counts cannot change any final access event. Averaging
    // those labels is harmless even when ID ties bias their physical survival.
    // Deferred labels have a stronger proof: swapping them leaves the complete
    // trace unchanged (including search retention), so their subsets are uniform.
    // Neither case reconstructs the identities of remaining deck cards.
    private ExactCalculationResult CountSearchHands(
        List<Card> cards,
        List<List<Card>> pools,
        Dictionary<State, BigInteger> terminal,
        BigInteger denominator,
        List<Combo> combos,
        IReadOnlyList<ComboGroup>? groups,
        bool totalOnly,
        SearchAccessPlan search,
        bool lower = false,
        bool declineSearch = false,
        SearchEventTemplate? template = null,
        bool packed = false,
        int[]? lossBudgets = null
    ) {
        int copies = lower ? pools.Sum(pool => pool.Sum(card => card.Copies)) : 0;
        CalculationResultRows resultRows = new(combos, groups, totalOnly);
        BigInteger[] wins = new BigInteger[resultRows.Count];
        CheckStorage(wins.Length, wins.Length * (WorkBudget.IntegerCells(denominator) + 1), "result accumulators");

        foreach ((State hand, BigInteger mass) in terminal) {
            int[] counts = hand.Values;

            if (packed) {
                budget.Spend(counts[0]);
                counts = DrawFrontierPacking.Unpack(counts);
            }

            List<Card> retained = [];
            List<SearchHandPool> random = [];

            for (int row = 0; row < cards.Count; row++) {
                budget.Spend(1);

                if (pools[row].Count == 1) {
                    retained.Add(pools[row][0].WithCopies(counts[row]));
                }
                else {
                    random.Add(new(pools[row], counts[row]));
                }
            }

            int loss = 0;

            if (lower) {
                budget.Spend(cards.Count + 1L);
                int held = retained.Sum(card => card.Copies) + random.Sum(pool => pool.Held);
                loss = copies - held - counts[^1];

                if (loss < 0 || held < 0 || counts[^1] < 0) {
                    throw new InvalidOperationException("Ordered search conservation failed.");
                }

                if (lossBudgets is not null) {
                    budget.Spend(1);
                    loss = lossBudgets[loss];
                }
            }

            SearchProbabilityCalculation calculation = new([.. random.SelectMany(pool => pool.Cards)],
                random.Sum(pool => pool.Held), retained, search, declineSearch ? 0 : counts[^1], budget, random, loss, template: template);
            ExactCalculationResult result = calculation.Calculate(combos, groups, totalOnly);
            ExactProbability[] values = result.ToRows();

            for (int i = 0; i < values.Length; i++) {
                ExactProbability probability = values[i];
                budget.Spend((WorkBudget.IntegerCells(mass) + probability.Cells + 1) * (probability.Cells + 1));
                BigInteger contribution = BigInteger.DivRem(mass * probability.Numerator, probability.Denominator, out BigInteger remainder);

                if (!remainder.IsZero) {
                    throw new InvalidOperationException("Conditional pool subsets must have equal integer permutation mass.");
                }

                wins[i] += contribution;

                if (lower) {
                    budget.Spend(wins.Length);
                    CheckStorage(wins.Length, wins.Sum(WorkBudget.IntegerCells), "result accumulators");
                }
            }
        }

        ExactProbability[] results = wins.Select(win => new ExactProbability(win, denominator)).ToArray();

        return resultRows.ToResult(results);
    }

    internal ExactCalculationResult CalculateSearchLower(
        List<Combo> combos,
        IReadOnlyList<ComboGroup>? groups,
        bool declineSearch,
        bool useKnownLoss = true
    ) {
        using IDisposable? stage = budget.SearchDiagnostics?.Stage(declineSearch ? "witness" : "lower");

        if (_streamedSearch is { } streamed) {
            return CountStreamedLower(streamed, combos, groups, declineSearch, useKnownLoss);
        }

        SearchCertificateState state = _searchCertificate ?? throw new InvalidOperationException("Missing completed ordered search distribution.");
        budget.Spend(4L * deck.Count + 3L * state.Cards.Count + 2L * state.Deferred.Count + 1);
        CheckStorage(deck.Count, 3L * deck.Count + state.Cards.Count, "certificate classes");
        // Reduction classes agree jointly on every final predicate, removal mask
        // and ORIGINAL search eligibility. Sources and direct-card distinctions
        // never merge. Their labels may be biased by ID-based discards: never
        // sample those original labels or apply the per-identity loss formula.
        // Instead n_C=h_C+r_C+d_C gives r_C >= max(0,n_C-h_C-D).
        // OrderedSearchLossBudget may replace D by a simultaneous bound on
        // searchable-class loss; eligibility agrees across every class member.
        // Every assignment within these shared class capacities lifts to distinct
        // physical targets and leaves all final predicates unchanged.
        Dictionary<string, Card> classes = state.Identities.ToDictionary(pair => pair.Key, pair => pair.Value[0].WithCopies(pair.Value.Sum(card => card.Copies)), StringComparer.Ordinal);
        List<List<Card>> pools = state.Cards.Select(card => new List<Card> { classes[card.Id] }).ToList();

        if (state.Deferred.Count > 0) {
            // Deferred classes may have DIFFERENT roles/search masks. Retain all
            // of them in the conditional pool. Whole-trace label invariance in
            // DeferUnobservedIdentities proves its uniform held subset, including
            // hidden positions, retries, custom priority and failed branches.
            pools[^1] = state.Deferred.Select(card => classes[card.Id]).ToList();
        }

        List<Card> searchCards = pools.SelectMany(pool => pool).ToList();
        SearchAccessPlan search = new(searchCards, budget);
        // Tighten this existing pass, without a second frontier or recount. The
        // caller has already completed the decline-search witness; failure or
        // exhaustion must retain it rather than publish partially counted rows.
        int[]? lossBudgets = declineSearch || !useKnownLoss ? null : OrderedSearchLossBudget.Create(searchCards, budget);

        SearchEventTemplate? template = CreateSearchTemplate(pools, combos, search);

        return CountSearchHands(state.Cards, pools, state.Terminal, state.Denominator,
            combos, groups, false, search, lower: true, declineSearch: declineSearch, template: template, packed: state.Packed,
            lossBudgets: lossBudgets);
    }

    private SearchEventTemplate? CreateSearchTemplate(List<List<Card>> pools, List<Combo> combos, SearchAccessPlan search) {
        if (!reuseSearchTemplates) {
            return null;
        }

        List<Card> randomRows = pools.Where(pool => pool.Count != 1).SelectMany(pool => pool)
            .OrderByDescending(card => card.Search is not null).ToList();
        List<Card> fixedRows = pools.Where(pool => pool.Count == 1).Select(pool => pool[0]).ToList();

        return new(search, combos, [.. randomRows, .. fixedRows], randomRows.Count, budget);
    }
}
