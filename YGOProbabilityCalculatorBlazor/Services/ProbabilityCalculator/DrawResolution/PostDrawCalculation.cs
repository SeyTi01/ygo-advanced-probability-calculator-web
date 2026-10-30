using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Certification;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Compilation;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.DrawResolution;

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
    bool deferSearchers = true,
    CardEffectRegistry? identityRegistry = null
) {
    // A quarter of Standard's cell allowance. Small conditional frontiers retain
    // their original representation; this is a trigger, never a raised limit.
    private CardEffectRegistry IdentityRegistry { get; } = identityRegistry ?? CardEffectRegistry.Default;
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
    // Paired tests retain the frontier path as a reference for streaming.
    internal bool StreamSearchTerminals { get; init; } = true;

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
        List<Card> deferred = deferIdentities && !cards.Any(card => card.IsDangerReveal) ? DeferUnobservedIdentities(ref cards) : [];
        OrderedDrawPreparation preparation = PrepareOrderedDraw(cards, combos);
        int n = cards.Count;
        int copies = preparation.Copies;
        OrderedDrawStateLayout layout = preparation.Layout;
        bool[] relevant = preparation.Relevant;
        BigInteger[] factorial = preparation.Factorial;

        int[] effects = Enumerable.Range(0, n).Where(index => cards[index].HasDrawEffect).ToArray();
        int[] filler = Enumerable.Range(0, n).Where(index => !relevant[index] && !cards[index].HasDrawEffect).ToArray();
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

        (bool[]?[] removalEligibility, bool[]?[]? costEligibility) = CompileHandOperations(cards);

        Route = deferred.Count > 0 ? SolverRoute.OrderedDeferred : SolverRoute.Ordered;
        OrderedResolver resolver = new(
            this,
            budget,
            cards,
            layout,
            preparation.CustomPriority,
            preparation.Ranks,
            relevant,
            removalEligibility,
            costEligibility
        );

        Dictionary<State, BigInteger> terminal = resolver.Resolve(handSize, copies, factorial, compactFrontierThreshold);
        TerminalStates = terminal.Count;
        BigInteger denominator = resolver.Denominator(factorial);

        return deferred.Count > 0
            ? CountDeferredHands(cards, deferred, terminal, denominator, combos, groups, totalOnly)
            : CountTerminalHands(cards, terminal, denominator, combos, groups, totalOnly);
    }

    private List<Card> ReduceDeck(List<Combo> combos) {
        ReductionSelectors selectors = PrepareReductionSelectors(combos);
        List<(Card Card, string Signature, string Eligibility)> entries = CreateReductionEntries(selectors);
        List<Card> reduced = [];
        bool danger = deck.Any(card => card.IsDangerReveal);
        foreach (IGrouping<string, (Card Card, string Signature, string Eligibility)> group in entries
            .GroupBy(entry => entry.Signature).OrderBy(group => group.Key, StringComparer.Ordinal)) {
            // Retain the established membership/ordinary/ID tie order. Only merge
            // adjacent ordinary runs with equal eligibility for every effect.
            // Combining interleaved IDs across different filters changes removals.
            string? previousEligibility = null;
            foreach ((Card card, string _, string eligibility) in group.Where(entry => !entry.Card.HasDrawEffect)
                .OrderBy(entry => entry.Card.Id, StringComparer.Ordinal)) {
                string observedEligibility = danger
                    ? eligibility + ":" + DangerCardIdentity.Resolve(card, IdentityRegistry)?.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : eligibility;
                if (observedEligibility == previousEligibility) {
                    reduced[^1] = reduced[^1].WithCopies(checked(reduced[^1].Copies + card.Copies));
                }
                else {
                    reduced.Add(card);
                    previousEligibility = observedEligibility;
                }
            }

            // Effect entries stay separate, preserving OPT identity AND custom rank.
            reduced.AddRange(group.Where(entry => entry.Card.HasDrawEffect).Select(entry => entry.Card)
                .OrderBy(card => card.Id, StringComparer.Ordinal));
        }

        return reduced;
    }
}
