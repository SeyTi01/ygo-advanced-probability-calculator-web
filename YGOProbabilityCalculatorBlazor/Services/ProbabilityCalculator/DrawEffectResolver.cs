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
    Dictionary<DrawComposition, ExactProbability> Scenarios,
    ExactProbability Exhaustion
);

internal static class DrawEffectResolver {
    internal static DrawResolution Resolve(int ordinaryCopies, List<Card> effects, int handSize, WorkBudget budget) {
        int totalCopies = ordinaryCopies;
        foreach (Card effect in effects) {
            budget.Spend(1);
            totalCopies = checked(totalCopies + effect.Copies);
        }

        if (handSize < 0 || handSize > totalCopies) {
            throw new ArgumentOutOfRangeException(nameof(handSize), "Opening hand size must fit the deck.");
        }

        WorkBudget.CheckStorage(1, effects.Count + 3L);
        Dictionary<DrawComposition, ExactProbability> current = new() {
            [new(0, new int[effects.Count])] = ExactProbability.One
        };
        Dictionary<DrawComposition, ExactProbability> scenarios = new();
        long scenarioCells = 0;
        ExactProbability exhaustion = ExactProbability.Zero;

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
                        pending += effects[i].DrawCount!.Value;
                    }
                }

                int remaining = totalCopies - encountered;
                if (pending == 0) {
                    int[] retained = state.EffectCounts.Select(count => Math.Max(0, count - 1)).ToArray();
                    Add(scenarios, new(state.OrdinaryCount, retained), weight, ref scenarioCells);
                    continue;
                }

                if (pending > remaining) {
                    // Even consuming every remaining position cannot fulfill current requests.
                    // Unseen effects can only add requests, so this is certain exhaustion.
                    exhaustion = exhaustion.Add(weight, budget);
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

        return new(scenarios, exhaustion);

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
            WorkBudget.CheckStorage(map.Count + (exists ? 0 : 1), cells);
            map[key] = value;
        }
    }
}
