using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Compilation;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Counting;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.DrawResolution;

internal sealed partial class PostDrawCalculation {
    internal int DeferredRows { get; private set; }

    private List<Card> DeferUnobservedIdentities(ref List<Card> cards, HashSet<string>? observedIdentities = null) {
        budget.Spend(cards.Count);
        Card[] effects = [.. cards.Where(card => card.Copies > 0 && card.DrawCount is not null)];

        if (effects.Length == 0 || !effects.All(DrawExecutionAssessment.CanDeferIdentities)
            || !effects.Any(effect => effect.PreDrawRemovalCount > 0 || effect.PostDrawDiscardCount > 0)) {
            return [];
        }

        CheckStorage(cards.Count + 1L, 5L * cards.Count + 1, "deferred identity partition");
        // Compile one selector at a time; retain only their union, not an E x N
        // table. Every source stays explicit, even if currently unpayable or used
        // as material by another source. A later draw can make it payable again.
        bool[] material = new bool[cards.Count];

        foreach (Card effect in effects) {
            if (effect.PreDrawRemovalCount > 0) {
                Include(effect.PreDrawSelector, cards);
            }
            if (effect.PostDrawDiscardCount > 0) {
                Include(effect.PostDrawSelector, cards);
            }
        }

        void Include(CardSelector selector, List<Card> originals) {
            bool[] eligible = CardSelectorCompiler.Compile(selector, originals, budget);
            budget.Spend(originals.Count);

            for (int i = 0; i < originals.Count; i++) {
                material[i] |= eligible[i];
            }
        }

        List<Card> deferred = [];
        List<Card> observed = [];

        for (int i = 0; i < cards.Count; i++) {
            budget.Spend(1);

            if (cards[i].Copies > 0 && cards[i].DrawCount is null && !material[i]
                && (observedIdentities is null || !observedIdentities.Contains(cards[i].Id))) {
                deferred.Add(cards[i]);
            }
            else {
                observed.Add(cards[i]);
            }
        }
        if (deferred.Count < 2) {
            return [];
        }

        // Keep the canonical relative order of every eligible identity and source.
        // No pooled card can be removed, activate, or affect a policy decision.
        // All sources' selectors are retained jointly, including source-copy
        // material. Swapping pool labels cannot change any source's payability,
        // relevance tiers among eligible cards, reserved source, pending FIFO,
        // custom rank, OPT usage, retry, batch completion or physical capacity.
        // Thus the same trace is followed jointly, not independently per effect.
        // Its representative is used only for capacity, never for combo evaluation.
        observed.Add(deferred[0].WithCopies(deferred.Sum(card => card.Copies)));
        cards = observed;
        DeferredRows = deferred.Count;

        return deferred;
    }

    private ExactCalculationResult CountDeferredHands(
        List<Card> cards,
        List<Card> deferred,
        Dictionary<State, BigInteger> terminal,
        BigInteger denominator,
        List<Combo> combos,
        IReadOnlyList<ComboGroup>? groups,
        bool totalOnly
    ) {
        // Relabeling physical copies inside the deferred pool leaves the complete
        // resolution trace unchanged. Given a terminal summary, its k held pool
        // cards are therefore a uniform k-subset, independent of the fixed hand.
        // Hidden positions are never conditioned on an identity. Permuting pool
        // labels across held, unseen and skipped positions preserves capacity,
        // all cost/removal choices and the activation schedule. The resolver's
        // (N - visibleDepth)! suffix counts those skipped positions as well, so
        // each k-subset has identical integer mass even after repeated milling.
        // Failed removals contribute no wins but remain in the original N! space.
        // The ordinary counter projects ALL combo constraints onto this subset;
        // the resolver's removal policy always used the full original combo list.
        CalculationResultRows resultRows = new(combos, groups, totalOnly);
        BigInteger[] wins = new BigInteger[resultRows.Count];
        CheckStorage(wins.Length, wins.Length * (WorkBudget.IntegerCells(denominator) + 1), "result accumulators");

        foreach ((State hand, BigInteger mass) in terminal) {
            List<Card> retained = [];

            for (int i = 0; i < cards.Count - 1; i++) {
                budget.Spend(1);
                if (hand.Values[i] > 0) {
                    retained.Add(cards[i].WithCopies(hand.Values[i]));
                }
            }

            // Each scenario owns and releases its counting caches. No persistent
            // memoization or dictionary of deferred physical hands is retained.
            ProbabilityCalculation calculation = new(deferred, hand.Values[^1], budget, retained);
            ExactCalculationResult result = totalOnly ? new(calculation.CalculateUnionExact(combos), [], [])
                : calculation.CalculateExactResults(combos, groups);

            Add(0, result.Total);

            for (int i = 0; i < result.Combos.Length; i++) {
                Add(resultRows.ComboRow(i), result.Combos[i]);
            }

            for (int i = 0; i < result.Groups.Length; i++) {
                Add(resultRows.GroupRow(i), result.Groups[i]);
            }

            void Add(int index, ExactProbability probability) {
                budget.Spend((WorkBudget.IntegerCells(mass) + probability.Cells + 1) * (probability.Cells + 1));
                BigInteger contribution = BigInteger.DivRem(mass * probability.Numerator, probability.Denominator, out BigInteger remainder);

                if (!remainder.IsZero) {
                    throw new InvalidOperationException("Deferred subsets must have equal integer permutation mass.");
                }

                wins[index] += contribution;
            }
        }

        ExactProbability[] results = [.. wins.Select(win => new ExactProbability(win, denominator))];

        return resultRows.ToResult(results);
    }
}
