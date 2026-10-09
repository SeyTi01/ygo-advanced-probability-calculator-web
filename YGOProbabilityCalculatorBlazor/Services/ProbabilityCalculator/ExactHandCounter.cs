using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

// Counts physical hands exactly, retaining counts and the denominator only
// for this request. Conversion uses the same exact numerator/denominator pair.
internal sealed class ExactHandCounter(List<Card> deck, int handSize, WorkBudget budget) {
    private readonly Dictionary<CompiledEvent, BigInteger> _counts = new();
    private int _cachedConstraints;
    private long _cachedIntegerCells;
    private BigInteger? _totalWays;

    internal double Probability(CompiledEvent? predicate) {
        if (predicate is null) {
            return 0;
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

            return handSize < 0 || handSize > deckSize ? double.NaN : 1;
        }

        BigInteger successes = Count(predicate);

        return ToProbability(successes);
    }

    internal double ToProbability(BigInteger successes) {
        BigInteger denominator = _totalWays!.Value;
        double doubleDenominator = (double)denominator;

        // Preserve ordinary-sized conversion, including the existing invalid
        // empty sample-space behavior. Valid counts satisfy 0 <= successes <= denominator.
        if (double.IsFinite(doubleDenominator)) {
            return (double)successes / doubleDenominator;
        }

        if (successes.IsZero) {
            return 0;
        }

        // Neither infinity/infinity nor finite/infinity represents the exact
        // ratio. Locate its binary exponent using integers, then round once
        // to a 53-bit significand (or the fixed 2^-1074 subnormal grid).
        long numeratorBits = successes.GetBitLength();
        long denominatorBits = denominator.GetBitLength();
        budget.Spend(1 + (numeratorBits + denominatorBits) / 32);
        long exponent = numeratorBits - denominatorBits;

        if (exponent < -1075) {
            return 0;
        }

        if ((successes << (int)-exponent) < denominator) {
            exponent--;
        }

        int shift = (int)Math.Min(1074, 52 - exponent);
        WorkBudget.CheckStorage(1, (numeratorBits + shift + 31) / 32);
        BigInteger significand = BigInteger.DivRem(successes << shift, denominator, out BigInteger remainder);
        int rounding = (remainder << 1).CompareTo(denominator);

        if (rounding > 0 || (rounding == 0 && !significand.IsEven)) {
            significand++;
        }

        return Math.ScaleB((double)significand, -shift);
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
            WorkBudget.CheckStorage(cardMasks.Count, (long)cardMasks.Count * (categories.Length + 1));
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
                bool satisfiesMinima = state.DrawnCards == handSize
                    && !categories.Where((category, i) => state.CategoryCounts[i] < category.MinCount).Any();

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
        WorkBudget.CheckStorage(maxDraw + 1L, maxDraw + 1L);
        BigInteger[] binomials = new BigInteger[maxDraw + 1];
        BigInteger binomial = 1;
        long binomialCells = maxDraw + 1L;

        for (int draw = 0; draw <= maxDraw; draw++) {
            budget.Spend(1 + binomial.GetBitLength() / 64);
            binomialCells += WorkBudget.IntegerCells(binomial);
            WorkBudget.CheckStorage(maxDraw + 1L, binomialCells);
            binomials[draw] = binomial;
            binomial = binomial * (groupSize - draw) / (draw + 1);
        }

        long nextIntegerCells = 0;

        foreach ((StateKey state, BigInteger ways) in states) {
            int limit = Math.Min(maxDraw, handSize - state.DrawnCards);

            foreach (int index in indices) {
                limit = Math.Min(limit, categories[index].MaxCount - state.CategoryCounts[index]);
            }

            int minimumDraw = Math.Max(0, handSize - state.DrawnCards - remainingCards);

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

                // Drop states whose minima cannot be reached by the remaining
                // eligible copies or remaining hand slots. Hall constraints
                // add dimensions, so early feasibility pruning matters.
                int slotsLeft = handSize - state.DrawnCards - draw;
                bool feasible = true;

                for (int i = 0; i < counts.Length; i++) {
                    if (counts[i] + Math.Min(slotsLeft, remaining[i]) < categories[i].MinCount) {
                        feasible = false;
                        break;
                    }
                }

                if (!feasible) {
                    continue;
                }

                StateKey key = new(state.DrawnCards + draw, counts);
                budget.Spend(1 + ways.GetBitLength() / 64 + binomials[draw].GetBitLength() / 64);
                BigInteger increment = ways * binomials[draw];
                bool existed = next.TryGetValue(key, out BigInteger previous);
                BigInteger updated = previous + increment;
                nextIntegerCells += WorkBudget.IntegerCells(updated) - (existed ? WorkBudget.IntegerCells(previous) : 0);
                int entries = next.Count + (existed ? 0 : 1);
                WorkBudget.CheckStorage(entries, (long)entries * categories.Length + nextIntegerCells);
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
            WorkBudget.CheckStorage(1, WorkBudget.IntegerCells(result));
        }

        return result;
    }

    // Counts are immutable after insertion; preserve structural state equality.
    private sealed record StateKey(int DrawnCards, int[] CategoryCounts) {
        public bool Equals(StateKey? other) => other is not null && DrawnCards == other.DrawnCards && CategoryCounts.AsSpan().SequenceEqual(other.CategoryCounts);

        public override int GetHashCode() {
            HashCode hash = new();
            hash.Add(DrawnCards);

            foreach (int count in CategoryCounts) {
                hash.Add(count);
            }

            return hash.ToHashCode();
        }
    }
}
