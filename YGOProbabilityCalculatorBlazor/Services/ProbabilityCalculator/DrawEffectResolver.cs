using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Counting;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Search;
using DrawResolutionResult = YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.DrawResolution.DrawResolution;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

// Arrays are owned by their key and never mutated after insertion.
internal sealed record DrawComposition(int OrdinaryCount, int[] EffectCounts) {
    public bool Equals(DrawComposition? other) => other is not null
        && OrdinaryCount == other.OrdinaryCount && EffectCounts.AsSpan().SequenceEqual(other.EffectCounts);

    public override int GetHashCode() {
        HashCode hash = new();
        hash.Add(OrdinaryCount);
        foreach (int count in EffectCounts) {
            hash.Add(count);
        }

        return hash.ToHashCode();
    }
}

internal static class DrawEffectResolver {
    internal static DrawResolutionResult Resolve(int ordinaryCopies, List<Card> effects, int handSize, WorkBudget budget) =>
        Resolve<VisibleComposition>(ordinaryCopies, effects, handSize, budget);

    internal static DrawResolutionResult Resolve<TTerminal>(int ordinaryCopies, List<Card> effects, int handSize, WorkBudget budget)
        where TTerminal : struct, IDrawTerminal {
        int totalCopies = ordinaryCopies;
        foreach (Card effect in effects) {
            budget.Spend(1);
            ArgumentOutOfRangeException.ThrowIfNegative(effect.Copies);
            totalCopies = checked(totalCopies + effect.Copies);
        }

        if (handSize < 0 || handSize > totalCopies) {
            throw new ArgumentOutOfRangeException(nameof(handSize), "Opening hand size must fit the deck.");
        }

        budget.Spend(effects.Count + 1L);
        if (!CanUseCountStateFastPath(effects, totalCopies, handSize)) {
            throw new InvalidOperationException("Exhaustion-sensitive draw effects require ordered resolution.");
        }

        budget.CheckStorage(1, effects.Count + 3L);
        Dictionary<DrawComposition, ExactProbability> current = new() {
            [new(0, new int[effects.Count])] = ExactProbability.One
        };
        Dictionary<DrawComposition, ExactProbability> scenarios = new();
        long scenarioCells = 0;

        while (current.Count > 0) {
            Dictionary<DrawComposition, ExactProbability> next = new();
            long nextCells = 0;

            foreach ((DrawComposition state, ExactProbability weight) in current) {
                budget.Spend(effects.Count + 1L);
                int encountered = state.OrdinaryCount;
                long pending = handSize - (long)state.OrdinaryCount;
                for (int i = 0; i < effects.Count; i++) {
                    int count = state.EffectCounts[i];
                    encountered = checked(encountered + count);
                    pending -= count;
                    if (count > 0) {
                        int activations = Math.Min(effects[i].MaximumActivations, count);
                        pending += (long)effects[i].DrawCount!.Value * activations;
                    }
                }

                int remaining = totalCopies - encountered;
                if (pending == 0) {
                    int[] retained = state.EffectCounts
                        .Select((count, index) => Math.Max(0, count - effects[index].MaximumActivations))
                        .ToArray();
                    Add(scenarios, new(state.OrdinaryCount, TTerminal.Retained(retained, state.EffectCounts, effects, remaining)), weight, ref scenarioCells);
                    continue;
                }

                if (pending >= remaining) {
                    // This count-state path is used only when every configured batch fits
                    // within the initial deck remainder. If all remaining cards are owed,
                    // the terminal composition is the whole remainder.
                    int[] retained = effects
                        .Select(effect => Math.Max(0, effect.Copies - effect.MaximumActivations))
                        .ToArray();
                    Add(scenarios, new(ordinaryCopies, TTerminal.Retained(retained, null, effects, 0)), weight, ref scenarioCells);
                    continue;
                }

                int ordinaryRemaining = ordinaryCopies - state.OrdinaryCount;
                if (ordinaryRemaining > 0) {
                    ExactProbability increment = weight.Multiply(new(ordinaryRemaining, remaining), budget);
                    Add(next, new(state.OrdinaryCount + 1, state.EffectCounts), increment, ref nextCells);
                }

                for (int i = 0; i < effects.Count; i++) {
                    int copies = effects[i].Copies - state.EffectCounts[i];
                    if (copies == 0) {
                        continue;
                    }

                    budget.Spend(effects.Count + 1L);
                    int[] counts = (int[])state.EffectCounts.Clone();
                    counts[i]++;
                    ExactProbability increment = weight.Multiply(new(copies, remaining), budget);
                    Add(next, new(state.OrdinaryCount, counts), increment, ref nextCells);
                }
            }

            current = next;
        }

        return new(scenarios);

        void Add(
            Dictionary<DrawComposition, ExactProbability> map,
            DrawComposition key,
            ExactProbability increment,
            ref long cells
        ) {
            budget.Spend(effects.Count + 1L);
            bool exists = map.TryGetValue(key, out ExactProbability previous);
            ExactProbability value = exists ? previous.Add(increment, budget) : increment;
            cells += value.Cells - (exists ? previous.Cells : 0) + (exists ? 0 : key.EffectCounts.Length + 1L);
            budget.CheckStorage(map.Count + (exists ? 0 : 1), cells);
            map[key] = value;
        }
    }

    internal static bool CanUseCountStateFastPath(IReadOnlyList<Card> effects, int totalCopies, int handSize) {
        if (effects.Any(effect => effect.IsDangerReveal)) {
            return false;
        }

        if (handSize < 0 || handSize > totalCopies) {
            return false;
        }

        // With no hand losses, draw >= 1 never shrinks the pre-activation hand.
        // Otherwise a minimum can change eligibility after a cost/removal.
        if (effects.Any(effect => effect.MinimumHandSize > 0)
            && (effects.Any(effect => effect.MinimumHandSize > handSize) || effects.Any(effect => effect.EffectPlan.RequiresHandResolution))) {
            return false;
        }

        // One OPT entry can only start drawing from the opening hand. Its own
        // first-activation requirement and later-draw lock add no restriction:
        // after that activation, OPT already prevents every remaining copy.
        // Distinct entries have separate OPT allowances, so this proof cannot
        // combine entries merely because their definitions or card IDs match.
        bool restrictionsAreRedundant = effects.Count == 1 && effects[0].MaximumActivations == 1
            && !effects[0].EffectPlan.RequiresHandResolution;
        if (!restrictionsAreRedundant
            && effects.Any(effect => effect.FirstActivationOnly || effect.BlocksLaterDraws)) {
            return false;
        }

        // If every possible activation fits collectively, hidden identities cannot
        // affect capacity or the schedule, whether hidden positions precede or follow a batch.
        // No observed identity or hand operation depends on those positions. Visible draws remain a uniform sample without replacement.
        long maximumDraws = 0;
        foreach (Card effect in effects) {
            int activations = Math.Min(effect.MaximumActivations, effect.Copies);
            maximumDraws += (effect.DrawCount!.Value + effect.PreDrawDeckRemovalCount + effect.PostDrawDeckMillCount) * (long)activations;
        }

        return maximumDraws <= totalCopies - handSize;
    }
}
