using YGOProbabilityCalculatorBlazor.Models;

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

internal sealed record DrawResolution(
    Dictionary<DrawComposition, ExactProbability> Scenarios
);

internal static class DrawEffectResolver {
    internal static DrawResolution Resolve(int ordinaryCopies, List<Card> effects, int handSize, WorkBudget budget) {
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
                        int activations = effects[i].DrawOncePerTurn ? 1 : count;
                        pending += (long)effects[i].DrawCount!.Value * activations;
                    }
                }

                int remaining = totalCopies - encountered;
                if (pending == 0) {
                    int[] retained = state.EffectCounts
                        .Select((count, index) => effects[index].DrawOncePerTurn ? Math.Max(0, count - 1) : 0)
                        .ToArray();
                    Add(scenarios, new(state.OrdinaryCount, retained), weight, ref scenarioCells);
                    continue;
                }

                if (pending >= remaining) {
                    // This count-state path is used only when every configured batch fits
                    // within the initial deck remainder. If all remaining cards are owed,
                    // the terminal composition is the whole remainder.
                    int[] retained = effects
                        .Select(effect => effect.DrawOncePerTurn ? Math.Max(0, effect.Copies - 1) : 0)
                        .ToArray();
                    Add(scenarios, new(ordinaryCopies, retained), weight, ref scenarioCells);
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
            cells += value.Cells - (exists ? previous.Cells : 0) + (exists ? 0 : effects.Count + 1L);
            budget.CheckStorage(map.Count + (exists ? 0 : 1), cells);
            map[key] = value;
        }
    }

    internal static bool CanUseCountStateFastPath(IReadOnlyList<Card> effects, int totalCopies, int handSize, bool conditionsEnabled = true) {
        if (handSize < 0 || handSize > totalCopies) {
            return false;
        }

        // Timing and draw locks invalidate the collective-contribution proof, even
        // for one identity: a copy drawn by another copy need not activate.
        if (conditionsEnabled && effects.Any(effect => effect.EffectPlan.HasActivationRestrictions)) {
            return false;
        }

        // If every possible activation fits collectively, hidden identities cannot
        // affect capacity or the schedule, whether hidden positions precede or follow a batch.
        // No observed identity or hand operation depends on those positions. Visible draws remain a uniform sample without replacement.
        long maximumDraws = 0;
        foreach (Card effect in effects) {
            int activations = effect.DrawOncePerTurn ? 1 : effect.Copies;
            maximumDraws += (effect.DrawCount!.Value + (conditionsEnabled ? effect.PreDrawDeckRemovalCount + effect.PostDrawDeckMillCount : 0)) * (long)activations;
        }

        return maximumDraws <= totalCopies - handSize;
    }
}
