using System.Numerics;
using System.Reflection;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public sealed class PhysicalSearchLossCounterTest {
    [TestCase(false)]
    [TestCase(true)]
    public void CapacityDistributionAndBooleanUnionsMatchIndependentLabeledSubsets(bool oncePerTurn) {
        CategoryBase a = new("A");
        CategoryBase b = new("B");
        List<Card> cards = [
            new([], 2, id: "s", search: new(CardSelector.For(a), false), oncePerTurn: oncePerTurn),
            new([], 1, id: "z", search: new(CardSelector.For(b), false)),
            new([a], 3, id: "a"),
            new([b], 2, id: "b"),
            new([a, b], 1, id: "ab"),
            new([], 2, id: "f")
        ];
        // These explicit target tables and predicates are the oracle's own
        // description, independent of search projection and combo compilation.
        int[][] targets = [[2, 4], [3, 4]];
        int[] limits = [oncePerTurn ? 1 : 2, 1];
        List<Combo> combos = [
            new([new(a, 1, 12)], "A"),
            new([new(b, 1, 12)], "B"),
            new([new(a, 1, 12), new(b, 1, 12)], "Distinct A and B"),
            new([new(a, 0, 0)], "No A"),
            new([new(a, 1, 1)], "Exactly one A"),
            new([], "No original source", cards: [new("s", 0, 0)])
        ];
        WorkBudget budget = new(CalculationWorkPolicy.Unlimited);
        PhysicalSearchLossCounter losses = new(cards, budget);
        PhysicalSearchHandCounter searches = new(cards, combos, budget);
        for (int variant = 0; variant < 18; variant++) {
            int[] held = [variant % 3, variant / 3 % 2, variant / 6, variant % 2, variant / 9, 1];
            int[] remaining = cards.Select((card, row) => card.Copies - held[row]).ToArray();
            if (variant % 4 == 0 && remaining[2] > 0) {
                remaining[2]--; // An observed consumed target cannot be hidden or fetched.
            }
            int[] maxima = MaximumFetches(held, targets, limits, cards.Count);
            for (int hidden = 0; hidden <= Math.Min(3, remaining.Sum()); hidden++) {
                Dictionary<string, BigInteger> expectedDistribution = new(StringComparer.Ordinal);
                BigInteger[] expectedWins = new BigInteger[8];
                BigInteger outcomes = BigInteger.Zero;
                EnumerateLabeledLosses(remaining, hidden, available => {
                    Add(expectedDistribution, CapacityKey(available, maxima), BigInteger.One);
                    bool[] success = EnumerateSearchWitnesses(held, available, targets, limits, Predicates);
                    AddWins(expectedWins, success, BigInteger.One);
                    outcomes++;
                });

                Dictionary<string, BigInteger> actualDistribution = new(StringComparer.Ordinal);
                BigInteger[] actualWins = new BigInteger[8];
                int[] beforeHeld = (int[])held.Clone();
                int[] beforeRemaining = (int[])remaining.Clone();
                losses.Enumerate(held, remaining, hidden, (available, weight) => {
                    Add(actualDistribution, string.Join(",", available), weight);
                    bool[] success = searches.Evaluate(held, available);
                    AddWins(actualWins, success, weight);
                });
                string name = $"variant {variant}, hidden {hidden}";
                Assert.Multiple(() => {
                    Assert.That(actualDistribution, Is.EquivalentTo(expectedDistribution), name + "/capacity distribution");
                    Assert.That(actualWins, Is.EqualTo(expectedWins), name + "/combo and union wins");
                    Assert.That(actualDistribution.Values.Aggregate(BigInteger.Zero, (sum, mass) => sum + mass),
                        Is.EqualTo(outcomes), name + "/complete mass");
                    Assert.That(held, Is.EqualTo(beforeHeld), name + "/held input");
                    Assert.That(remaining, Is.EqualTo(beforeRemaining), name + "/remaining input");
                });
            }
        }
        Assert.That(budget.PeakCells, Is.LessThanOrEqualTo(CalculationStoragePolicy.Standard.CellLimit));

        static bool[] Predicates(int[] hand) {
            int countA = hand[2] + hand[4];
            int countB = hand[3] + hand[4];
            // Distinct roles require two labels, even when one AB matches both.
            bool distinct = countA > 0 && countB > 0 && hand[2] + hand[3] + hand[4] >= 2;
            return [countA >= 1, countB >= 1, distinct, countA == 0, countA == 1, hand[0] == 0];
        }
    }

    [Test]
    public void NoOriginalSearcherCollapsesEveryHiddenSubsetToOneRepresentative() {
        CategoryBase target = new("Target");
        List<Card> cards = [new([], 1, id: "s", search: new(CardSelector.For(target), false)),
            new([target], 3, id: "a"), new([], 8, id: "f")];
        WorkBudget budget = new(CalculationWorkPolicy.Unlimited);
        PhysicalSearchLossCounter losses = new(cards, budget);
        int callbacks = 0;
        BigInteger mass = BigInteger.Zero;
        losses.Enumerate([0, 0, 1], [1, 3, 7], 3, (available, weight) => {
            callbacks++;
            Assert.That(available, Is.EqualTo(new[] { 0, 0, 0 }));
            mass += weight;
        });
        BigInteger expected = BigInteger.Zero;
        EnumerateLabeledLosses([1, 3, 7], 3, _ => expected++);
        Assert.Multiple(() => {
            Assert.That(callbacks, Is.EqualTo(1));
            Assert.That(mass, Is.EqualTo(expected));
            Assert.That(expected, Is.EqualTo(new BigInteger(165)));
        });
    }

    [Test]
    public void FetchedSearchSourceRemainsInertAndSaturationBoundaryIsInclusive() {
        CategoryBase fetched = new("Fetched source");
        CategoryBase target = new("Target");
        List<Card> cards = [new([], 1, id: "s", search: new(CardSelector.For(fetched), false)),
            new([fetched], 3, id: "z", search: new(CardSelector.For(target), false)),
            new([target], 3, id: "a"), new([], 2, id: "f")];
        WorkBudget budget = new(CalculationWorkPolicy.Unlimited);
        PhysicalSearchLossCounter losses = new(cards, budget);
        PhysicalSearchHandCounter searches = new(cards, [new([new(target, 1, 5)], "Target")], budget);
        int callbacks = 0;
        losses.Enumerate([1, 0, 0, 0], [0, 3, 3, 2], 2, (available, _) => {
            callbacks++;
            Assert.That(available, Is.EqualTo(new[] { 0, 1, 0, 0 }));
            Assert.That(searches.Evaluate([1, 0, 0, 0], available)[0], Is.False);
        });
        Assert.That(callbacks, Is.EqualTo(1), "r_z - H = M_z is saturated for every hidden subset");

        HashSet<int> capacities = [];
        losses.Enumerate([1, 0, 0, 0], [0, 2, 3, 2], 2, (available, _) => capacities.Add(available[1]));
        Assert.That(capacities, Is.EquivalentTo(new[] { 0, 1 }), "one below the boundary retains target loss");
    }

    [Test]
    public void HiddenLossTableAndRecursiveWeightsHonorExactWorkAndStorageThresholds() {
        CategoryBase target = new("Target");
        List<Card> cards = [
            new([], 2, id: "source", search: new(CardSelector.For(target), false)),
            new([target], 35, id: "target-a"), new([target], 5, id: "target-b"), new([], 18, id: "filler")
        ];
        WorkBudget measured = new(CalculationWorkPolicy.Unlimited);
        BigInteger expected = 2 * Choose(58, 34) + Choose(60, 30);
        Assert.That(Run(measured, out long initialEntries, out long initialCells), Is.EqualTo(expected));
        Assert.Multiple(() => {
            Assert.That(measured.PeakEntries, Is.GreaterThan(initialEntries), "Recursive loss frames participate in the entry bound.");
            Assert.That(measured.PeakCells, Is.GreaterThan(initialCells), "Retained integer payload and frames exceed the constructor payload.");
            Assert.That(Choose(35, 17), Is.GreaterThan(new BigInteger(uint.MaxValue)), "The parent binomial cursor needs multiple cells.");
            Assert.That(measured.PeakCells, Is.LessThanOrEqualTo(CalculationStoragePolicy.Standard.CellLimit));
        });

        CalculationStoragePolicy exactStorage = Storage(measured.PeakEntries, measured.PeakCells);
        WorkBudget exact = new(new(measured.Spent), exactStorage);
        Assert.That(Run(exact, out _, out _), Is.EqualTo(expected));
        Assert.That(exact.Spent, Is.EqualTo(measured.Spent));
        ProbabilityCalculationLimitException workFailure = Assert.Throws<ProbabilityCalculationLimitException>(
            () => Run(new(new(measured.Spent - 1), exactStorage), out _, out _)
        )!;
        Assert.That(workFailure.Reason, Is.EqualTo(ProbabilityCalculationLimitReason.Work));
        foreach (bool cells in new[] { false, true }) {
            CalculationStoragePolicy tooSmall = Storage(measured.PeakEntries - (cells ? 0 : 1), measured.PeakCells - (cells ? 1 : 0));
            ProbabilityCalculationLimitException storageFailure = Assert.Throws<ProbabilityCalculationLimitException>(
                () => Run(new(CalculationWorkPolicy.Unlimited, tooSmall), out _, out _)
            )!;
            Assert.That(storageFailure.Reason, Is.EqualTo(ProbabilityCalculationLimitReason.Storage));
        }

        BigInteger Run(WorkBudget budget, out long constructorEntries, out long constructorCells) {
            PhysicalSearchLossCounter losses = new(cards, budget);
            constructorEntries = budget.PeakEntries;
            constructorCells = budget.PeakCells;
            BigInteger mass = BigInteger.Zero;
            losses.Enumerate([2, 0, 0, 0], [0, 35, 5, 18], 34, (_, weight) => mass += weight);
            // Populate the retained binomial table with multi-cell values, then
            // revisit the recursive case while that table's payload stays live.
            losses.Enumerate([0, 0, 0, 0], [2, 35, 5, 18], 30, (_, weight) => mass += weight);
            losses.Enumerate([2, 0, 0, 0], [0, 35, 5, 18], 34, (_, weight) => mass += weight);
            return mass;
        }

        static BigInteger Choose(int count, int selected) {
            BigInteger numerator = BigInteger.One;
            BigInteger denominator = BigInteger.One;
            for (int factor = 1; factor <= selected; factor++) {
                numerator *= count - factor + 1;
                denominator *= factor;
            }
            return numerator / denominator;
        }
    }

    private static CalculationStoragePolicy Storage(long entries, long cells) {
        ConstructorInfo constructor = typeof(CalculationStoragePolicy).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            [typeof(CalculationStorageTier), typeof(long), typeof(long)],
            null
        )!;
        return (CalculationStoragePolicy)constructor.Invoke([CalculationStorageTier.Standard, entries, cells]);
    }

    private static void EnumerateLabeledLosses(int[] remaining, int hidden, Action<int[]> terminal) {
        List<int> labels = [];
        for (int row = 0; row < remaining.Length; row++) {
            for (int copy = 0; copy < remaining[row]; copy++) {
                labels.Add(row);
            }
        }
        int[] available = (int[])remaining.Clone();
        Visit(0, hidden);

        void Visit(int start, int left) {
            if (left == 0) {
                terminal(available);
                return;
            }
            for (int label = start; label <= labels.Count - left; label++) {
                available[labels[label]]--;
                Visit(label + 1, left - 1);
                available[labels[label]]++;
            }
        }
    }

    private static bool[] EnumerateSearchWitnesses(
        int[] held,
        int[] available,
        int[][] targets,
        int[] limits,
        Func<int[], bool[]> predicates
    ) {
        List<int> originalLabels = [];
        for (int source = 0; source < targets.Length; source++) {
            for (int copy = 0; copy < Math.Min(held[source], limits[source]); copy++) {
                originalLabels.Add(source);
            }
        }
        int[] hand = (int[])held.Clone();
        int[] deck = (int[])available.Clone();
        bool[] success = new bool[predicates(hand).Length];
        Visit(0);
        return success;

        void Visit(int label) {
            if (label == originalLabels.Count) {
                bool[] matches = predicates(hand);
                for (int combo = 0; combo < success.Length; combo++) {
                    success[combo] |= matches[combo];
                }
                return;
            }
            Visit(label + 1); // Searches are optional, including for 0/0 bounds.
            int source = originalLabels[label];
            foreach (int target in targets[source]) {
                if (deck[target] == 0) {
                    continue;
                }
                hand[source]--;
                hand[target]++;
                deck[target]--;
                Visit(label + 1);
                deck[target]++;
                hand[target]--;
                hand[source]++;
            }
        }
    }

    private static int[] MaximumFetches(int[] held, int[][] targets, int[] limits, int rows) {
        int[] maxima = new int[rows];
        for (int source = 0; source < targets.Length; source++) {
            foreach (int target in targets[source]) {
                maxima[target] += Math.Min(held[source], limits[source]);
            }
        }
        return maxima;
    }

    private static string CapacityKey(int[] available, int[] maxima) =>
        string.Join(",", available.Select((count, row) => Math.Min(count, maxima[row])));

    private static void Add(Dictionary<string, BigInteger> distribution, string key, BigInteger mass) =>
        distribution[key] = distribution.GetValueOrDefault(key) + mass;

    private static void AddWins(BigInteger[] wins, bool[] success, BigInteger mass) {
        if (success.Any(value => value)) {
            wins[0] += mass;
        }
        for (int combo = 0; combo < success.Length; combo++) {
            if (success[combo]) {
                wins[combo + 1] += mass;
            }
        }
        if (success[0] || success[1]) {
            wins[^1] += mass;
        }
    }
}
