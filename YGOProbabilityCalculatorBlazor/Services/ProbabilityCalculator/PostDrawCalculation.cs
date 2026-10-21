using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

// A layer counts visible labeled prefixes of equal length. Hidden removals leave
// identities unobserved: their counts stay in the identity pool, while a separate
// total reduces physical capacity. Exchangeability makes each next visible label
// uniform in that pool. Unobserved suffixes contribute (N - depth)!, including
// hidden positions, so all outcomes share N! even when physical capacity is zero.
// No physical permutations are enumerated and no probability mass is rounded.
internal sealed partial class PostDrawCalculation(
    List<Card> deck,
    int handSize,
    WorkBudget budget,
    DrawActivationPriority? priority = null,
    bool deferIdentities = true,
    int compactFrontierThreshold = PostDrawCalculation.DefaultCompactFrontierThreshold,
    bool eagerTerminalAggregation = true,
    bool reuseSearchTemplates = true,
    bool compactSearchTerminals = true,
    bool deferSearchers = true
) {
    // A quarter of Standard's cell allowance. Small conditional frontiers retain
    // their original representation; this is a trigger, never a raised limit.
    internal const int DefaultCompactFrontierThreshold = 65_536;
    private bool CompactPhysicalTerminals => compactSearchTerminals;
    private bool EagerTerminalAggregation { get; } = eagerTerminalAggregation;
    internal int TerminalStates { get; private set; }
    internal SolverRoute Route { get; private set; }
    internal int PeakStates { get; private set; }
    internal long PeakCells { get; private set; }
    internal string? LargestStructure { get; private set; }
    internal int ReducedRows { get; private set; }
    internal int PackedLayers { get; private set; }
    // Completed-layer payload sum, not a process-memory or per-structure limit.
    internal long PeakConcurrentCells { get; private set; }
    // Opt-in profiling only: called while old/new layers and terminal hands coexist.
    internal Action? ObserveFrontier { get; init; }
    // Opt-in physical-outcome audit; mutable buffers are copied for the observer.
    internal Action<IReadOnlyList<Card>, int[], int[], BigInteger>? ObservePhysicalOutcome { get; init; }
    // Optional terminal-distribution audit; observers receive copies, never keys.
    internal Action<int[], BigInteger>? ObserveTerminal { get; init; }
    // Internal reference switch for paired algorithm tests and diagnostic runs.
    // Forced Streaming always retains the original entry-word implementation.
    internal bool AggregatePhysicalBatches { get; init; } = true;

    private void CheckStorage(long entries, long cells, string structure) {
        if (cells > PeakCells) {
            PeakCells = cells;
            LargestStructure = structure;
        }
        budget.CheckStorage(entries, cells);
    }

    // Owned immutable vectors: remaining counts, hand counts, only actual OPT flags,
    // batch draws left, pending completion source (index + 1), optional hidden-card count
    // and activation state, then FIFO candidate entry indices. Hidden identities remain in the first vector.
    private sealed record State(int[] Values) {
        public bool Equals(State? other) => other is not null && Values.AsSpan().SequenceEqual(other.Values);

        public override int GetHashCode() {
            HashCode hash = new();
            foreach (int value in Values) {
                hash.Add(value);
            }
            return hash.ToHashCode();
        }
    }

    internal ExactCalculationResult Calculate(List<Combo> combos, IReadOnlyList<ComboGroup>? groups, bool totalOnly = false) {
        List<Card> cards = ReduceDeck(combos);
        List<Card> deferred = deferIdentities ? DeferUnobservedIdentities(ref cards) : [];
        int n = cards.Count;
        bool customPriority = priority?.Mode == DrawActivationMode.CustomPriority;
        if (customPriority) {
            budget.Spend(n + (long)priority!.CardIds.Count);
            budget.CheckStorage(n, n);
        }
        int[] ranks = customPriority ? cards.Select(card => priority!.Rank(card.Id)).ToArray() : [];
        ReducedRows = n;
        OrderedDrawStateLayout layout = new(cards);
        int copies = 0;
        foreach (Card card in cards) {
            budget.Spend(1);
            ArgumentOutOfRangeException.ThrowIfNegative(card.Copies);
            copies = checked(copies + card.Copies);
        }
        if (handSize < 0 || handSize > copies) {
            throw new ArgumentOutOfRangeException(nameof(handSize));
        }

        CheckStorage(1, layout.QueueStart + (long)copies + 1, "policy descriptors");
        foreach (Combo combo in combos) {
            long categoryAtoms = combo.AllCategories.Sum(requirement => (long)requirement.Selector.CategoryIdentities.Count);
            long deckMemberships = n + cards.Sum(card => (long)card.Categories.Count);
            budget.Spend((combo.AllCards.Count() + categoryAtoms + 1L) * deckMemberships);
        }
        bool[] relevant = cards.Select(card => combos.Any(combo =>
            combo.AllCards.Any(requirement => requirement.MinCount > 0 && requirement.CardId == card.Id)
            || combo.AllCategories.Any(requirement => requirement.MinCount > 0 && requirement.Selector.Matches(card)))).ToArray();

        CheckStorage(copies + 1L, copies + 1L, "factorials");
        BigInteger[] factorial = new BigInteger[copies + 1];
        factorial[0] = 1;
        long factorialCells = 1;
        for (int i = 1; i <= copies; i++) {
            budget.Spend(WorkBudget.IntegerCells(factorial[i - 1]) + 1);
            factorial[i] = factorial[i - 1] * i;
            factorialCells += WorkBudget.IntegerCells(factorial[i]);
            CheckStorage(copies + 1L, factorialCells, "factorials");
        }

        int[] effects = Enumerable.Range(0, n).Where(index => cards[index].DrawCount is not null).ToArray();
        int[] filler = Enumerable.Range(0, n).Where(index => !relevant[index] && cards[index].DrawCount is null).ToArray();
        // A single effect identity has no order dimension under either priority policy. For a hand-neutral,
        // irrelevant, non-OPT effect, relevant ordinary cards always retain the
        // best handSize copies seen so far. Keep this optimization only when all
        // possible batches fit; exhaustion can change both the final hand size and
        // the effect schedule, which the general resolver handles exactly.
        if (effects.Length == 1 && filler.Length <= 1) {
            int effect = effects[0];
            if (DrawExecutionAssessment.IsExchangeableEffect(cards[effect], relevant[effect], copies, handSize)) {
                Route = SolverRoute.Exchangeable;
                return CalculateExchangeable(cards, relevant, effect, filler.FirstOrDefault(-1), factorial, combos, groups, totalOnly);
            }
        }

        CheckStorage(n, n, "removal selector table");
        bool[]?[] removalEligibility = new bool[n][];
        long eligibilityCells = n;
        for (int i = 0; i < n; i++) {
            if (cards[i].PostDrawDiscardCount > 0 && cards[i].PostDrawSelector.Kind != CardSelectorKind.Any) {
                eligibilityCells += n;
                CheckStorage(n, eligibilityCells, "removal selector table");
                removalEligibility[i] = CardSelectorCompiler.Compile(cards[i].PostDrawSelector, cards, budget);
            }
        }

        // Keep post-draw-only decks free of cost tables.
        bool[]?[]? costEligibility = null;
        if (cards.Any(card => card.PreDrawRemovalCount > 0)) {
            eligibilityCells += n;
            CheckStorage(n, eligibilityCells, "removal selector table");
            costEligibility = new bool[n][];
            for (int i = 0; i < n; i++) {
                if (cards[i].PreDrawRemovalCount > 0 && cards[i].PreDrawSelector.Kind != CardSelectorKind.Any) {
                    eligibilityCells += n;
                    CheckStorage(n, eligibilityCells, "removal selector table");
                    costEligibility[i] = CardSelectorCompiler.Compile(cards[i].PreDrawSelector, cards, budget);
                }
            }
        }

        Route = deferred.Count > 0 ? SolverRoute.OrderedDeferred : SolverRoute.Ordered;
        OrderedResolver resolver = new(
            this,
            budget,
            cards,
            layout,
            customPriority,
            ranks,
            relevant,
            removalEligibility,
            costEligibility
        );
        Dictionary<State, BigInteger> terminal = resolver.Resolve(handSize, copies, factorial, compactFrontierThreshold);
        TerminalStates = terminal.Count;

        return deferred.Count > 0
            ? CountDeferredHands(cards, deferred, terminal, factorial[copies], combos, groups, totalOnly)
            : CountTerminalHands(cards, terminal, factorial[copies], combos, groups, totalOnly);
    }

    private List<Card> ReduceDeck(List<Combo> combos) {
        // Canonical tie order: membership bit string for ordinal category identities,
        // then ordinal direct-card IDs (including maximum-only selectors), ordinary
        // before effect, then ordinal Card.Id, then earlier encounter within an ID.
        // Ordinary cards with equal signatures are indistinguishable to every
        // requirement and to the policy. Their counts can therefore be merged.
        long selectors = 0;
        foreach (Combo combo in combos) {
            selectors += combo.Categories.Sum(requirement => (long)requirement.Selector.CategoryIdentities.Count) + combo.Cards.Count;
            foreach (ComboAlternativeGroup group in combo.AlternativeGroups) {
                selectors += group.Alternatives.Sum(alternative => alternative.Category is { } category
                    ? (long)category.Selector.CategoryIdentities.Count : 1L);
            }
            CheckStorage(selectors, selectors, "policy selectors");
            budget.Spend(combo.RequirementCount + 1L);
        }
        CheckStorage(deck.Count, (selectors + 1) * deck.Count, "policy descriptors");
        budget.Spend(selectors * (selectors + 1) + deck.Count * (long)(deck.Count + 1) * (selectors + 1));
        CategoryBase[] categories = combos.SelectMany(combo => combo.AllCategories)
            .SelectMany(requirement => requirement.CategoryBases).Distinct().OrderBy(category => category.Identity, StringComparer.Ordinal).ToArray();
        string[] direct = combos.SelectMany(combo => combo.AllCards).Select(requirement => requirement.CardId)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        List<bool[]> filters = [];
        foreach (Card source in deck) {
            if (source.Copies > 0 && source.PostDrawDiscardCount > 0 && source.PostDrawSelector.Kind != CardSelectorKind.Any) {
                CheckStorage(filters.Count + 1L, (filters.Count + 1L) * deck.Count, "reduction selectors");
                filters.Add(CardSelectorCompiler.Compile(source.PostDrawSelector, deck, budget));
            }
        }
        foreach (Card source in deck) {
            if (source.Copies > 0 && source.PreDrawRemovalCount > 0 && source.PreDrawSelector.Kind != CardSelectorKind.Any) {
                CheckStorage(filters.Count + 1L, (filters.Count + 1L) * deck.Count, "reduction selectors");
                filters.Add(CardSelectorCompiler.Compile(source.PreDrawSelector, deck, budget));
            }
        }
        List<(Card Card, string Signature, string Eligibility)> entries = [];
        int row = 0;
        foreach (Card card in deck) {
            ArgumentOutOfRangeException.ThrowIfNegative(card.Copies);
            budget.Spend((categories.Length + 1L) * (card.Categories.Count + 1L) + direct.Length);
            CheckStorage(deck.Count, (categories.Length + (long)direct.Length + 1) * deck.Count, "policy descriptors");
            string signature = string.Concat(categories.Select(category => card.Categories.Contains(category) ? '1' : '0'))
                + string.Concat(direct.Select(id => card.Id == id ? '1' : '0'));
            budget.Spend(filters.Count);
            CheckStorage(deck.Count, (categories.Length + (long)direct.Length + filters.Count + 1) * deck.Count, "policy descriptors");
            entries.Add((card, signature, string.Concat(filters.Select(filter => filter[row] ? '1' : '0'))));
            row++;
        }
        List<Card> reduced = [];
        foreach (IGrouping<string, (Card Card, string Signature, string Eligibility)> group in entries
            .GroupBy(entry => entry.Signature).OrderBy(group => group.Key, StringComparer.Ordinal)) {
            // Retain the established membership/ordinary/ID tie order. Only merge
            // adjacent ordinary runs with equal eligibility for every effect.
            // Combining interleaved IDs across different filters changes removals.
            string? previousEligibility = null;
            foreach ((Card card, string _, string eligibility) in group.Where(entry => entry.Card.DrawCount is null)
                .OrderBy(entry => entry.Card.Id, StringComparer.Ordinal)) {
                if (eligibility == previousEligibility) {
                    reduced[^1] = reduced[^1].WithCopies(checked(reduced[^1].Copies + card.Copies));
                }
                else {
                    reduced.Add(card);
                    previousEligibility = eligibility;
                }
            }
            // Effect entries stay separate, preserving OPT identity AND custom rank.
            reduced.AddRange(group.Where(entry => entry.Card.DrawCount is not null).Select(entry => entry.Card)
                .OrderBy(card => card.Id, StringComparer.Ordinal));
        }
        return reduced;
    }
}
