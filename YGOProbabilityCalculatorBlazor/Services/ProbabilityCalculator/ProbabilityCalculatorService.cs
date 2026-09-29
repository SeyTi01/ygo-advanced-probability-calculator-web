using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

public class ProbabilityCalculatorService : IProbabilityCalculatorService {
    public double CalculateProbabilityForCombos(List<Card> deck, List<Combo> combos, int handSize) {
        ValidateComboCount(combos);
        ValidateCardIds(deck);
        var calculation = new Calculation(deck, handSize);
        return calculation.Union(combos.Select(combo => calculation.Canonicalize(combo)).ToList());
    }

    public ProbabilityCalculationResult CalculateProbabilityResults(
        List<Card> deck, List<Combo> combos, int handSize, IReadOnlyList<ComboGroup>? groups = null) {
        ValidateComboCount(combos);
        ValidateCardIds(deck);
        // Cache and work budget belong to this request, never to a service instance.
        var calculation = new Calculation(deck, handSize);
        var events = combos.Select(combo => calculation.Canonicalize(combo)).ToList();
        var totalProbability = calculation.Union(events);
        var comboProbabilities = combos.Select((combo, index) =>
            new ComboProbabilityResult(index, combo.Name, calculation.Probability(events[index]), combo.GroupId)).ToList();
        var groupProbabilities = (groups ?? []).Select(group => {
            var members = events.Where((_, index) => combos[index].GroupId == group.Id).ToList();
            return new GroupProbabilityResult(group.Id, group.Name, calculation.Union(members), members.Count);
        }).ToList();

        return new ProbabilityCalculationResult(
            totalProbability, comboProbabilities.AsReadOnly(), groupProbabilities.AsReadOnly());
    }

    private static void ValidateComboCount(List<Combo> combos) {
        if (combos.Count > IProbabilityCalculatorService.MaxComboCount)
            throw new ArgumentOutOfRangeException(nameof(combos),
                $"Calculation supports at most {IProbabilityCalculatorService.MaxComboCount} combos.");
    }

    private static void ValidateCardIds(List<Card> deck) {
        if (deck.Select(card => card.Id).Distinct(StringComparer.Ordinal).Count() != deck.Count)
            throw new ArgumentException("Deck card IDs must be unique.", nameof(deck));
    }

    private sealed class Calculation(List<Card> deck, int handSize) {
        private readonly WorkBudget budget = new();
        private readonly Dictionary<Event, double> probabilities = new();
        private int cachedConstraints;
        private BigInteger? totalWays;

        public Event? Canonicalize(Combo combo) {
            budget.Spend(combo.Categories.Count + combo.Cards.Count + 1L);
            var constraints = new List<Requirement>();
            foreach (var group in combo.Categories.GroupBy(c => c.BaseCategory.Name, StringComparer.Ordinal)) {
                var min = group.Max(c => c.MinCount);
                var max = Math.Min(handSize, group.Min(c => c.MaxCount));
                if (min > max) return null;
                // Every category count is in [0, handSize], regardless of overlap.
                if (min == 0 && max == handSize) continue;
                constraints.Add(new Requirement(new ConstraintKey(false, group.Key), min, max));
            }
            foreach (var group in combo.Cards.GroupBy(c => c.CardId, StringComparer.Ordinal)) {
                var min = group.Max(c => c.MinCount);
                var max = Math.Min(handSize, group.Min(c => c.MaxCount));
                if (min > max) return null;
                if (min == 0 && max == handSize) continue;
                constraints.Add(new Requirement(new ConstraintKey(true, group.Key), min, max));
            }
            return new Event(constraints.OrderBy(c => c.Key.IsCard).ThenBy(c => c.Key.Value, StringComparer.Ordinal).ToArray());
        }

        public double Union(List<Event?> events) {
            var universal = events.FirstOrDefault(e => e is { Constraints.Length: 0 });
            if (universal is not null) return Probability(universal);
            var terms = new Dictionary<Event, long>();
            long storedConstraints = 0;
            foreach (var nextEvent in events.Where(e => e is not null).Distinct()) {
                var current = nextEvent!;
                // Indicator identity: U OR E = U + E - U*E. Equal intersections
                // combine integer coefficients before any floating-point evaluation.
                // Nested events cancel here too; no independence assumption is used.
                var changes = new Dictionary<Event, long>();
                long changedConstraints = 0;
                Add(changes, current, 1, ref changedConstraints);
                foreach (var (existing, coefficient) in terms) {
                    var intersection = Intersect(existing, current);
                    if (intersection is not null)
                        Add(changes, intersection, -coefficient, ref changedConstraints);
                }
                foreach (var (intersection, coefficient) in changes)
                    Add(terms, intersection, coefficient, ref storedConstraints);
            }

            double probability = 0;
            foreach (var (intersection, coefficient) in terms)
                probability += coefficient * Probability(intersection);
            return probability;
        }

        private void Add(Dictionary<Event, long> terms, Event key, long coefficient, ref long storedConstraints) {
            budget.Spend(key.Constraints.Length + 1L);
            if (terms.TryGetValue(key, out var previous)) {
                var updated = previous + coefficient;
                if (updated == 0) {
                    terms.Remove(key);
                    storedConstraints -= key.Constraints.Length;
                } else terms[key] = updated;
            } else if (coefficient != 0) {
                WorkBudget.CheckStorage(terms.Count + 1, storedConstraints + key.Constraints.Length);
                terms.Add(key, coefficient);
                storedConstraints += key.Constraints.Length;
            }
        }

        private Event? Intersect(Event first, Event second) {
            budget.Spend(first.Constraints.Length + second.Constraints.Length + 1L);
            var constraints = new List<Requirement>();
            int i = 0, j = 0;
            while (i < first.Constraints.Length || j < second.Constraints.Length) {
                if (i == first.Constraints.Length) { constraints.Add(second.Constraints[j++]); continue; }
                if (j == second.Constraints.Length) { constraints.Add(first.Constraints[i++]); continue; }
                var left = first.Constraints[i];
                var right = second.Constraints[j];
                var order = left.Key.IsCard.CompareTo(right.Key.IsCard);
                if (order == 0) order = StringComparer.Ordinal.Compare(left.Key.Value, right.Key.Value);
                if (order < 0) { constraints.Add(left); i++; }
                else if (order > 0) { constraints.Add(right); j++; }
                else {
                    var min = Math.Max(left.MinCount, right.MinCount);
                    var max = Math.Min(left.MaxCount, right.MaxCount);
                    if (min > max) return null;
                    constraints.Add(new Requirement(left.Key, min, max));
                    i++; j++;
                }
            }
            return new Event(constraints.ToArray());
        }

        public double Probability(Event? predicate) {
            if (predicate is null) return 0;
            if (probabilities.TryGetValue(predicate, out var cached)) return cached;
            var categories = predicate.Constraints;
            // Build one mask per card entry, not per physical copy. Adding Copies
            // preserves multiplicities while avoiding an expanded deck allocation.
            var cardMasks = new Dictionary<BigInteger, int>();
            var deckSize = 0;
            foreach (var card in deck) {
                if (card.Copies < 0) throw new ArgumentOutOfRangeException(nameof(deck), "Copies cannot be negative.");
                if (card.Copies == 0) continue;
                deckSize = checked(deckSize + card.Copies);
                budget.Spend((long)(categories.Length + 1) * (card.Categories.Count + 1));
                var mask = BigInteger.Zero;
                for (var i = 0; i < categories.Length; i++)
                    if (categories[i].Key.IsCard ? card.Id == categories[i].Key.Value :
                        card.Categories.Any(c => c.Name == categories[i].Key.Value))
                        mask |= BigInteger.One << i;
                cardMasks[mask] = cardMasks.GetValueOrDefault(mask) + card.Copies;
                WorkBudget.CheckStorage(cardMasks.Count, (long)cardMasks.Count * (categories.Length + 1));
            }

            double result;
            if (categories.Length == 0) {
                result = handSize < 0 || handSize > deckSize ? double.NaN : 1;
            } else {
                if (totalWays is null) {
                    budget.Spend(Math.Max(0, Math.Min(handSize, deckSize - handSize)) + 1L);
                    totalWays = ComputeBinomial(deckSize, handSize, budget);
                }
                var states = new Dictionary<StateKey, double> { [new(0, new int[categories.Length])] = 1 };
                foreach (var (mask, count) in cardMasks)
                    states = Convolve(states, mask, count, categories);
                BigInteger successes = 0;
                foreach (var (state, ways) in states) {
                    budget.Spend(categories.Length + 1L);
                    if (state.DrawnCards == handSize &&
                        !categories.Where((category, i) => state.CategoryCounts[i] < category.MinCount).Any())
                        successes += (BigInteger)ways;
                }
                result = (double)successes / (double)totalWays.Value;
            }
            // A full cache only stops retaining entries; it never changes the result.
            if (probabilities.Count < 1024 && cachedConstraints + categories.Length <= 16384) {
                probabilities.Add(predicate, result);
                cachedConstraints += categories.Length;
            }
            return result;
        }

        private Dictionary<StateKey, double> Convolve(
            Dictionary<StateKey, double> states, BigInteger pattern, int groupSize, Requirement[] categories) {
            var next = new Dictionary<StateKey, double>();
            var indices = Enumerable.Range(0, categories.Length)
                .Where(i => (pattern & (BigInteger.One << i)) != 0).ToArray();
            var maxDraw = Math.Min(groupSize, handSize);
            // The same binomial row is used by every state in this convolution.
            budget.Spend(maxDraw + 1L);
            WorkBudget.CheckStorage(maxDraw + 1, maxDraw + 1L);
            var binomials = new double[maxDraw + 1];
            BigInteger binomial = 1;
            for (var draw = 0; draw <= maxDraw; draw++) {
                budget.Spend(1 + binomial.GetBitLength() / 64);
                binomials[draw] = (double)binomial;
                binomial = binomial * (groupSize - draw) / (draw + 1);
            }
            foreach (var (state, ways) in states) {
                var limit = Math.Min(maxDraw, handSize - state.DrawnCards);
                foreach (var index in indices)
                    limit = Math.Min(limit, categories[index].MaxCount - state.CategoryCounts[index]);
                for (var draw = 0; draw <= limit; draw++) {
                    budget.Spend(categories.Length + 1L);
                    var counts = (int[])state.CategoryCounts.Clone();
                    foreach (var index in indices) counts[index] += draw;
                    var key = new StateKey(state.DrawnCards + draw, counts);
                    var increment = ways * binomials[draw];
                    if (next.TryGetValue(key, out var previous)) next[key] = previous + increment;
                    else {
                        WorkBudget.CheckStorage(next.Count + 1, (long)(next.Count + 1) * categories.Length);
                        next.Add(key, increment);
                    }
                }
            }
            return next;
        }
    }

    // Bound retained entries/count-vector cells and cumulative work, rather than
    // pretending the 30-combo compatibility ceiling is a runtime guarantee.
    // Each live map: <= 32K entries and 256K cells (about 1 MiB of int payload).
    // Old/new DP maps and union/change maps can coexist. Object overhead is extra.
    private sealed class WorkBudget {
        private long remaining = 10_000_000;
        public void Spend(long units) {
            remaining -= units;
            if (remaining < 0) throw new ProbabilityCalculationLimitException();
        }
        public static void CheckStorage(int entries, long cells) {
            if (entries > 32768 || cells > 262144) throw new ProbabilityCalculationLimitException();
        }
    }

    private static BigInteger ComputeBinomial(int n, int k, WorkBudget budget) {
        if (k < 0 || k > n) return 0;
        k = Math.Min(k, n - k);
        BigInteger result = 1;
        for (var i = 1; i <= k; i++) {
            budget.Spend(1 + result.GetBitLength() / 64);
            result = result * (n - (k - i)) / i;
        }
        return result;
    }

    private readonly record struct ConstraintKey(bool IsCard, string Value);
    private readonly record struct Requirement(ConstraintKey Key, int MinCount, int MaxCount);

    private sealed record Event(Requirement[] Constraints) {
        public bool Equals(Event? other) => other is not null &&
            Constraints.AsSpan().SequenceEqual(other.Constraints);
        public override int GetHashCode() {
            var hash = new HashCode();
            foreach (var constraint in Constraints) hash.Add(constraint);
            return hash.ToHashCode();
        }
    }

    // Counts are immutable after insertion; preserve structural state equality.
    private sealed record StateKey(int DrawnCards, int[] CategoryCounts) {
        public bool Equals(StateKey? other) => other is not null &&
            DrawnCards == other.DrawnCards && CategoryCounts.AsSpan().SequenceEqual(other.CategoryCounts);
        public override int GetHashCode() {
            var hash = new HashCode();
            hash.Add(DrawnCards);
            foreach (var count in CategoryCounts) hash.Add(count);
            return hash.ToHashCode();
        }
    }
}
