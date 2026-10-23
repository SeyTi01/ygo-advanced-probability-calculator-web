using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using DrawResolutionResult = YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.DrawResolution.DrawResolution;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Counting;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Search;

// Necessary event only: every original copy of each unconditional direct-card
// requirement with min == copies must escape hidden loss. Other roles, maxima
// and OR groups can only restrict success further; alternatives alone establish
// no mandatory identity. Distinct entries are matched by ID, never by name.
//
// Domain: the ALREADY COMPLETED count-state distribution, one draw-source entry,
// no hand operations, all batches fit. With N cards, k ordinary cards held,
// r retained sources, R remaining cards and b hidden cards per activation,
// a = (N-k-r-R)/(1+b), e=r+a, H=a*b. Thus the coarser Fast terminal uniquely
// recovers the full observed source count e; no new draw resolution is needed.
//
// Conditional on that terminal, the ordinary hand is a uniform k-subset of O.
// Hidden positions are a uniform H-subset of U=N-k-e UNOBSERVED copies, excluding
// consumed as well as retained sources. For T mandatory ordinary copies:
//   sum_h C(T,h) C(O-T,k-h) C(U-T+h,H) / [C(O,k) C(U,H)].
// This counts their JOINT survival, including held/drawn targets. The existing
// count-state guard makes the hidden positions unobserved and exchangeable even
// when banish precedes drawing. It does not make individual losses independent.
// No sufficient success claim, search assignment or physical solver is used.
internal static class TargetSurvivalUpperBound {
    private const int MaximumCopies = 64;
    private const int MaximumScenarios = 64;
    private const int MaximumIdentitiesPerCombo = 4;
    private const int MaximumRows = 30;
    private const long MaximumAdditionalWork = 100_000;
    private const long PublicationReserve = 4_096;
    private const long MaximumCells = 4_096;

    internal static ExactCalculationResult? TryCalculate(
        List<Card> cards,
        List<Card> effects,
        DrawResolutionResult? resolution,
        List<Combo> combos,
        IReadOnlyList<ComboGroup>? groups,
        ExactCalculationResult upper,
        WorkBudget request
    ) {
        if (resolution is null || effects.Count != 1 || cards.Count > MaximumCopies
            || resolution.Scenarios.Count > MaximumScenarios || combos.Count > MaximumRows
            || groups?.Count > MaximumRows) {
            return null;
        }
        Card source = effects[0];
        int hiddenPerActivation = source.PreDrawDeckRemovalCount + source.PostDrawDeckMillCount;
        if (hiddenPerActivation == 0 || source.EffectPlan.RequiresHandResolution) {
            return null;
        }
        // A child allowance is carved from the EXISTING request, with publication
        // headroom left intact. Every spent unit is debited even on a failed proof.
        long allowance = request.Remaining is long remaining
            ? Math.Min(MaximumAdditionalWork, remaining - PublicationReserve)
            : MaximumAdditionalWork;
        if (allowance <= 0) {
            return null;
        }
        WorkBudget budget = new(new(allowance), request.StoragePolicy);
        try {
            return Calculate();
        }
        finally {
            request.Spend(budget.Spent);
            request.CheckStorage(budget.PeakEntries, budget.PeakCells);
        }

        ExactCalculationResult? Calculate() {
            budget.Spend(cards.Count + combos.Count + 1L);
            int copies = cards.Sum(card => card.Copies);
            if (copies > MaximumCopies) {
                return null;
            }
            int ordinary = copies - source.Copies;
            int[] targets = new int[combos.Count];
            Check(targets.Length, targets.Length);
            for (int row = 0; row < combos.Count; row++) {
                // Cap even the eligibility scan; do not traverse a large role tree.
                if (combos[row].Cards.Count > MaximumIdentitiesPerCombo) {
                    continue;
                }
                HashSet<string> identities = new(StringComparer.Ordinal);
                foreach (ComboCard requirement in combos[row].Cards) {
                    budget.Spend(cards.Count + 1L);
                    Card? card = cards.Find(card => card.Id == requirement.CardId);
                    if (card is not null && card.Id != source.Id && requirement.MinCount == card.Copies
                        && identities.Add(card.Id)) {
                        targets[row] += card.Copies;
                    }
                }
            }
            if (!targets.Any(count => count > 0)) {
                return null;
            }
            // Upper bound on combinatorial loop iterations before any counting.
            long branches = (long)resolution!.Scenarios.Count * targets.Where(count => count > 0).Sum(count => count + 1);
            if (branches > 1_024 || branches * (3 * copies + 1) > MaximumAdditionalWork) {
                return null;
            }
            ExactProbability[] sums = new ExactProbability[combos.Count];
            Array.Fill(sums, ExactProbability.Zero);
            foreach ((DrawComposition composition, ExactProbability weight) in resolution.Scenarios) {
                budget.Spend(combos.Count + 1L);
                int held = composition.OrdinaryCount;
                int retained = composition.EffectCounts[0];
                int lost = copies - held - retained - composition.EffectCounts[1];
                int activations = lost / (1 + hiddenPerActivation);
                int seen = retained + activations;
                int hidden = activations * hiddenPerActivation;
                int unseen = copies - held - seen;
                if (lost < 0 || lost % (1 + hiddenPerActivation) != 0 || seen > source.Copies
                    || activations != Math.Min(source.MaximumActivations, seen) || hidden > unseen) {
                    throw new InvalidOperationException("Single-source count-state conservation failed.");
                }
                budget.Spend(25); // Two binomials with at most 64 bits each.
                BigInteger denominator = Choose(ordinary, held) * Choose(unseen, hidden);
                for (int row = 0; row < sums.Length; row++) {
                    if (targets[row] == 0) {
                        continue;
                    }
                    BigInteger numerator = 0;
                    for (int h = Math.Max(0, held - ordinary + targets[row]); h <= Math.Min(held, targets[row]); h++) {
                        BigInteger first = Choose(targets[row], h);
                        BigInteger second = Choose(ordinary - targets[row], held - h);
                        BigInteger third = Choose(unseen - targets[row] + h, hidden);
                        long cells = WorkBudget.IntegerCells(first) + WorkBudget.IntegerCells(second) + WorkBudget.IntegerCells(third) + 1;
                        budget.Spend(cells * cells);
                        numerator += first * second * third;
                    }
                    sums[row] = sums[row].Add(weight.Multiply(new(numerator, denominator), budget), budget);
                    Check(sums.Length + targets.Length, targets.Length + sums.Sum(value => value.Cells) + weight.Cells + 64);
                }
            }
            bool improved = false;
            for (int row = 0; row < sums.Length; row++) {
                sums[row] = targets[row] == 0 ? upper.Combos[row] : Minimum(upper.Combos[row], sums[row]);
                improved |= sums[row] != upper.Combos[row];
            }
            if (!improved) {
                return null;
            }
            // Boole's inequality, not an independence assumption: P(union E_i)
            // <= sum P(E_i) <= sum U_i. Unsupported rows keep their old U_i.
            ExactProbability total = ExactProbability.Zero;
            ExactProbability[] grouped = new ExactProbability[groups?.Count ?? 0];
            Array.Fill(grouped, ExactProbability.Zero);
            for (int row = 0; row < sums.Length; row++) {
                total = total.Add(sums[row], budget);
                for (int group = 0; group < grouped.Length; group++) {
                    budget.Spend(1);
                    if (combos[row].GroupId == groups![group].Id) {
                        grouped[group] = grouped[group].Add(sums[row], budget);
                    }
                }
            }
            for (int group = 0; group < grouped.Length; group++) {
                grouped[group] = Minimum(upper.Groups[group], grouped[group]);
            }
            Check(sums.Length + grouped.Length + targets.Length + 1,
                targets.Length + sums.Sum(value => value.Cells) + grouped.Sum(value => value.Cells) + total.Cells);
            return new(Minimum(upper.Total, total), sums, grouped);
        }

        BigInteger Choose(int n, int k) {
            budget.Spend(1);
            if (k < 0 || n < k) {
                return BigInteger.Zero;
            }
            k = Math.Min(k, n - k);
            BigInteger value = BigInteger.One;
            for (int i = 1; i <= k; i++) {
                long cells = WorkBudget.IntegerCells(value) + 2;
                budget.Spend(cells * cells);
                value = value * (n - k + i) / i;
            }
            return value;
        }

        ExactProbability Minimum(ExactProbability old, ExactProbability candidate) {
            long cells = old.Cells + candidate.Cells + 1;
            budget.Spend(cells * cells);
            return candidate.Numerator * old.Denominator < old.Numerator * candidate.Denominator ? candidate : old;
        }

        void Check(long entries, long cells) {
            if (entries > 128 || cells > MaximumCells) {
                throw new ProbabilityCalculationLimitException(ProbabilityCalculationLimitReason.Storage);
            }
            budget.CheckStorage(entries, cells);
        }
    }
}
