using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Compilation;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Counting;

// Counts physical hands exactly, retaining counts and the denominator only
// for this request. Conversion uses the same exact numerator/denominator pair.
internal sealed class ExactHandCounter(List<Card> deck, int handSize, WorkBudget budget) {
    private readonly Dictionary<CompiledEvent, BigInteger> _counts = new();
    private int _cachedConstraints;
    private long _cachedIntegerCells;
    private BigInteger? _totalWays;

    internal ExactProbability Fraction(BigInteger successes) => new(successes, _totalWays!.Value);

    internal ExactProbability ProbabilityExact(CompiledEvent? predicate) {
        if (predicate is null) {
            return ExactProbability.Zero;
        }

        // Preserve the cheap universal event, including decks whose
        // binomial denominator would exceed floating-point range.
        if (predicate.Constraints.Length == 0) {
            int deckSize = 0;

            foreach (Card card in deck) {
                budget.Spend(1);

                if (card.Copies < 0) {
                    throw new ArgumentOutOfRangeException(nameof(deck), "Copies cannot be negative.");
                }

                deckSize = checked(deckSize + card.Copies);
            }

            return handSize < 0 || handSize > deckSize ? new(0, 0) : ExactProbability.One;
        }

        return Fraction(Count(predicate));
    }

    internal BigInteger Count(CompiledEvent predicate) {
        if (_counts.TryGetValue(predicate, out BigInteger cached)) {
            return cached;
        }

        CountBound[] categories = predicate.Constraints;
        // Build one mask per card entry, not per physical copy. Adding Copies
        // preserves multiplicities while avoiding an expanded deck allocation.
        Dictionary<BigInteger, int> cardMasks = new();
        int deckSize = 0;

        for (int row = 0; row < deck.Count; row++) {
            Card card = deck[row];

            if (card.Copies < 0) {
                throw new ArgumentOutOfRangeException(nameof(deck), "Copies cannot be negative.");
            }

            if (card.Copies == 0) {
                continue;
            }

            deckSize = checked(deckSize + card.Copies);
            budget.Spend((long)(categories.Length + 1) * (card.Categories.Count + 1));
            BigInteger mask = BigInteger.Zero;

            for (int i = 0; i < categories.Length; i++) {
                if ((categories[i].EligibleRows & (BigInteger.One << row)) != 0) {
                    mask |= BigInteger.One << i;
                }
            }

            cardMasks[mask] = cardMasks.GetValueOrDefault(mask) + card.Copies;
            budget.CheckStorage(cardMasks.Count, (long)cardMasks.Count * (categories.Length + 1));
        }

        if (_totalWays is null) {
            budget.Spend(Math.Max(0, Math.Min(handSize, deckSize - handSize)) + 1L);
            _totalWays = ComputeBinomial(deckSize, handSize, budget);
        }

        BigInteger successes = 0;

        if (categories.Length == 0) {
            successes = _totalWays.Value;
        }
        else {
            // Keep combinatorial weights integral, including beyond 2^53.
            Dictionary<StateKey, BigInteger> states = new() { [new(0, new int[categories.Length])] = 1 };
            int[] remaining = new int[categories.Length];

            foreach ((BigInteger mask, int count) in cardMasks) {
                for (int i = 0; i < remaining.Length; i++) {
                    if ((mask & (BigInteger.One << i)) != 0) {
                        remaining[i] += count;
                    }
                }
            }

            int remainingCards = deckSize;

            foreach ((BigInteger mask, int count) in cardMasks) {
                remainingCards -= count;

                for (int i = 0; i < remaining.Length; i++) {
                    if ((mask & (BigInteger.One << i)) != 0) {
                        remaining[i] -= count;
                    }
                }

                states = Convolve(states, mask, count, categories, remaining, remainingCards);
            }

            foreach ((StateKey state, BigInteger ways) in states) {
                budget.Spend(categories.Length + 1L);
                bool satisfiesMinima = state.DrawnCards == handSize && !categories.Where((category, i) => state.CategoryCounts[i] < category.MinCount).Any();

                if (satisfiesMinima) {
                    successes += ways;
                }
            }
        }

        // A full cache only stops retaining entries; it never changes the result.
        long integerCells = WorkBudget.IntegerCells(successes);
        bool canRetainCount = _counts.Count < 1024
            && _cachedConstraints + categories.Length <= 16384
            && _cachedIntegerCells + integerCells <= 262144;

        if (canRetainCount) {
            _counts.Add(predicate, successes);
            _cachedConstraints += categories.Length;
            _cachedIntegerCells += integerCells;
        }

        return successes;
    }

    private Dictionary<StateKey, BigInteger> Convolve(
        Dictionary<StateKey, BigInteger> states,
        BigInteger pattern,
        int groupSize,
        CountBound[] categories,
        int[] remaining,
        int remainingCards
    ) {
        Dictionary<StateKey, BigInteger> next = new();
        int[] indices = Enumerable.Range(0, categories.Length)
            .Where(index => (pattern & (BigInteger.One << index)) != 0)
            .ToArray();

        int maxDraw = Math.Min(groupSize, handSize);

        // The same binomial row is used by every state in this convolution.
        budget.Spend(maxDraw + 1L);
        // Check the length in long arithmetic before int addition/allocation.
        budget.CheckStorage(maxDraw + 1L, maxDraw + 1L);
        BigInteger[] binomials = new BigInteger[maxDraw + 1];
        BigInteger binomial = 1;
        long binomialCells = maxDraw + 1L;

        for (int draw = 0; draw <= maxDraw; draw++) {
            budget.Spend(1 + binomial.GetBitLength() / 64);
            binomialCells += WorkBudget.IntegerCells(binomial);
            budget.CheckStorage(maxDraw + 1L, binomialCells);
            binomials[draw] = binomial;
            binomial = binomial * (groupSize - draw) / (draw + 1);
        }

        long nextIntegerCells = 0;

        foreach ((StateKey state, BigInteger ways) in states) {
            int slots = handSize - state.DrawnCards;
            int limit = Math.Min(maxDraw, slots);
            int minimumDraw = Math.Max(0, slots - remainingCards);
            int affected = 0;
            // Solve reachability before allocating a candidate vector. For a
            // matching row, deficit <= draw + min(slots - draw, remaining)
            // iff deficit <= slots AND draw >= deficit - remaining. For an
            // unmatched row, deficit <= remaining AND draw <= slots - deficit.
            // Saturating an unrestricted count at its minimum preserves both.
            budget.Spend(categories.Length + 1L);

            for (int i = 0; i < categories.Length; i++) {
                int deficit = categories[i].MinCount - state.CategoryCounts[i];

                if (affected < indices.Length && indices[affected] == i) {
                    affected++;

                    if (deficit > slots) {
                        limit = -1;
                        break;
                    }

                    minimumDraw = Math.Max(minimumDraw, deficit - remaining[i]);
                    limit = Math.Min(limit, categories[i].MaxCount - state.CategoryCounts[i]);
                }
                else {
                    if (deficit > remaining[i]) {
                        limit = -1;
                        break;
                    }

                    limit = Math.Min(limit, slots - deficit);
                }
            }

            for (int draw = minimumDraw; draw <= limit; draw++) {
                budget.Spend(categories.Length + 1L);
                int[] counts = (int[])state.CategoryCounts.Clone();

                foreach (int index in indices) {
                    int count = counts[index] + draw;

                    // With no restrictive maximum, counts above the minimum
                    // are equivalent for all future transitions.
                    counts[index] = categories[index].MaxCount == handSize
                        ? Math.Min(count, categories[index].MinCount)
                        : count;
                }

                StateKey key = new(state.DrawnCards + draw, counts);
                budget.Spend(1 + ways.GetBitLength() / 64 + binomials[draw].GetBitLength() / 64);
                BigInteger increment = ways * binomials[draw];
                bool existed = next.TryGetValue(key, out BigInteger previous);
                BigInteger updated = previous + increment;
                nextIntegerCells += WorkBudget.IntegerCells(updated) - (existed ? WorkBudget.IntegerCells(previous) : 0);
                int entries = next.Count + (existed ? 0 : 1);
                budget.CheckStorage(entries, (long)entries * categories.Length + nextIntegerCells);
                next[key] = updated;
            }
        }

        return next;
    }

    private static BigInteger ComputeBinomial(int n, int k, WorkBudget budget) {
        if (k < 0 || k > n) {
            return 0;
        }

        k = Math.Min(k, n - k);
        BigInteger result = 1;

        for (int i = 1; i <= k; i++) {
            budget.Spend(1 + result.GetBitLength() / 64);
            result = result * (n - (k - i)) / i;
            budget.CheckStorage(1, WorkBudget.IntegerCells(result));
        }

        return result;
    }

    // Counts are immutable after insertion; preserve structural state equality.
    private sealed record StateKey(int DrawnCards, int[] CategoryCounts) {
        // Lookup and insertion revisit the same owned vector. Cache only its
        // hash; equality must still compare every count, including collisions.
        private readonly int _hashCode = ComputeHash(DrawnCards, CategoryCounts);

        public bool Equals(StateKey? other) => other is not null && DrawnCards == other.DrawnCards && CategoryCounts.AsSpan().SequenceEqual(other.CategoryCounts);

        public override int GetHashCode() => _hashCode;

        private static int ComputeHash(int drawnCards, int[] categoryCounts) {
            HashCode hash = new();
            hash.Add(drawnCards);

            foreach (int count in categoryCounts) {
                hash.Add(count);
            }

            return hash.ToHashCode();
        }
    }
}
