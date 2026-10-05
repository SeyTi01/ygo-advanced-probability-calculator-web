using System.Diagnostics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public class ProbabilityCalculatorServiceTest
{
    private const double Tolerance = 1e-12;
    private ProbabilityCalculatorService _probabilityCalculator;

    [SetUp]
    public void Setup()
    {
        _probabilityCalculator = new ProbabilityCalculatorService();
    }

    [Test]
    public void AllCategoriesMaxZero()
    {
        CategoryBase categoryA = new("A");
        List<Card> deck =
        [
            new([categoryA], 2),
            new([], 2)
        ];

        Combo combo = new([new ComboCategory(categoryA, 0, 0)]);
        double probability = _probabilityCalculator.CalculateProbabilityForCombos(deck, [combo], 2);

        Assert.That(probability, Is.EqualTo(1.0 / 6.0).Within(Tolerance));
    }

    [Test]
    public void MultipleRangedCategories_ZeroToHandSize_ShouldBe100Percent()
    {
        const int handSize = 5;

        CategoryBase categoryA = new("A");
        CategoryBase categoryB = new("B");
        CategoryBase categoryC = new("C");

        List<Card> deck =
        [
            new([categoryA, categoryB], 3),
            new([categoryB, categoryC], 3),
            new([categoryA, categoryC], 3),
            new([categoryA], 3),
            new([categoryB], 3),
            new([categoryC], 3),
            new([categoryA, categoryB, categoryC], 2),
            new([], 20)
        ];

        Combo combo = new([
            new ComboCategory(categoryA, 0, handSize),
            new ComboCategory(categoryB, 0, handSize),
            new ComboCategory(categoryC, 0, handSize)
        ]);

        double probability = _probabilityCalculator.CalculateProbabilityForCombos(deck, [combo], handSize);
        Assert.That(probability, Is.EqualTo(1.0).Within(Tolerance));
    }

    [Test]
    public void CalculateProbabilityForCombos_SubsetScenario_EqualsSingleComboProbability()
    {
        CategoryBase categoryA = new("A");
        CategoryBase categoryB = new("B");

        List<Card> deck =
        [
            new([categoryA]),
            new([categoryA]),
            new([categoryA]),
            new([categoryB]),
            new([categoryB]),
            new([categoryB])
        ];

        const int handSize = 2;

        Combo comboA = new([new ComboCategory(categoryA, 1, 1)]);
        Combo comboAb = new([
            new ComboCategory(categoryA, 1, 1),
            new ComboCategory(categoryB, 1, 1)
        ]);

        double probA = _probabilityCalculator.CalculateProbabilityForCombos(deck, [comboA], handSize);
        double probBoth = _probabilityCalculator.CalculateProbabilityForCombos(deck, [comboA, comboAb], handSize);

        Assert.That(probBoth, Is.EqualTo(probA).Within(Tolerance));
    }

    [Test]
    public void CalculateProbabilityForCombos_ExampleScenario_CalculatedCorrectly()
    {
        CategoryBase categoryA = new("A");
        CategoryBase categoryB = new("B");
        CategoryBase categoryC = new("C");

        List<Card> cards =
        [
            new([categoryA]),
            new([categoryB]),
            new([categoryC]),
            new([])
        ];

        Combo combo1 = new([new ComboCategory(categoryA, 1, 1)]);
        Combo combo2 = new([
            new ComboCategory(categoryB, 1, 1),
            new ComboCategory(categoryC, 1, 1)
        ]);

        double probability = _probabilityCalculator.CalculateProbabilityForCombos(cards, [combo1, combo2], 2);
        Assert.That(probability, Is.EqualTo(0.5 + 1.0 / 6.0).Within(Tolerance));
    }

    [Test]
    public void ExactRangeRequirements_CalculateCorrectProbability()
    {
        CategoryBase starterCat = new("starter");
        CategoryBase extenderCat = new("extender");

        List<Card> deck =
        [
            new([starterCat], 2),
            new([extenderCat]),
            new([starterCat, extenderCat])
        ];

        Combo combo = new([
            new ComboCategory(starterCat, 1, 1),
            new ComboCategory(extenderCat, 1, 1)
        ]);

        double probability = _probabilityCalculator.CalculateProbabilityForCombos(deck, [combo], 2);
        // Only the two starter-only + extender-only hands meet both exact-one limits.
        Assert.That(probability, Is.EqualTo(2.0 / 6.0).Within(Tolerance));
    }

    [Test]
    public void MinGreaterThanOneRequirements_CalculatedCorrectly()
    {
        const int handSize = 3;

        CategoryBase starterCat = new("starter");

        List<Card> deck =
        [
            new([starterCat], 3),
            new([], 2)
        ];

        Combo combo = new([new ComboCategory(starterCat, 2, handSize)]);

        double probability = _probabilityCalculator.CalculateProbabilityForCombos(deck, [combo], handSize);
        Assert.That(probability, Is.EqualTo(7.0 / 10.0).Within(Tolerance));
    }

    [Test]
    public void OverlapThreeCategories_CalculatedCorrectly()
    {
        const int handSize = 3;

        CategoryBase starterCat = new("starter");
        CategoryBase extenderCat = new("extender");
        CategoryBase comboCat = new("combo");

        List<Card> deck =
        [
            new([starterCat]),
            new([extenderCat]),
            new([comboCat]),
            new([starterCat, extenderCat, comboCat]),
            new([])
        ];

        Combo combo = new([
            new ComboCategory(starterCat, 1, handSize),
            new ComboCategory(extenderCat, 1, handSize),
            new ComboCategory(comboCat, 1, handSize)
        ]);

        double probability = _probabilityCalculator.CalculateProbabilityForCombos(deck, [combo], handSize);
        // Exhaustive physical-hand slot assignment: the four 3-subsets of the
        // four eligible copies pass; any hand containing the blank fails.
        Assert.That(probability, Is.EqualTo(4.0 / 10.0).Within(Tolerance));
    }

    [Test]
    public void SixtyCardDeckMatchesExactHypergeometricFractions()
    {
        CategoryBase a = new("A");
        // Exact Wolfram evaluations: 1-C(57,5)/C(60,5),
        // C(30,15)^2/C(60,30), and 1/C(60,30), respectively.
        AssertProbability([new([a], 3), new([], 57)], new([new(a, 1, 5)]), 5, 1597.0 / 6844.0);
        AssertProbability([new([a], 30), new([], 30)], new([new(a, 15, 15)]), 30, 4655852362800.0 / 22884013460693.0);
        AssertProbability([new([a], 30), new([], 30)], new([new(a, 30, 30)]), 30, 1.0 / 118264581564861424.0);

        void AssertProbability(List<Card> deck, Combo combo, int handSize, double expected)
        {
            double actual = _probabilityCalculator.CalculateProbabilityForCombos(deck, [combo], handSize);
            // Relative tolerance matters for the ~8.46e-18 rare event; an absolute
            // 1e-12 tolerance would incorrectly accept zero.
            Assert.That(actual, Is.EqualTo(expected).Within(expected * Tolerance));
            Assert.That(double.IsFinite(actual) && actual is >= 0 and <= 1, Is.True);
        }
    }

    [TestCase(8)]
    [TestCase(16)]
    public void RepeatedOverlappingEventsDoNotAccumulateMaterialCancellationError(int comboCount)
    {
        CategoryBase a = new("A");
        List<Card> deck = [new([a], 3), new([], 37)];
        List<Combo> combos =
            [.. Enumerable.Range(0, comboCount).Select(_ => new Combo(new[] { new ComboCategory(a, 1, 5) }))];
        Stopwatch timer = Stopwatch.StartNew();
        double actual = _probabilityCalculator.CalculateProbabilityForCombos(deck, combos, 5);
        TestContext.Out.WriteLine(
            $"40 cards / {comboCount} duplicate combos: {timer.Elapsed.TotalMilliseconds:F1} ms; error {actual - 667.0 / 1976.0:E3}");
        // Wolfram: 1-C(37,5)/C(40,5), irrespective of duplicate count.
        Assert.That(actual, Is.EqualTo(667.0 / 1976.0).Within(Tolerance));
        Assert.That(double.IsFinite(actual) && actual is >= 0 and <= 1, Is.True);
    }

    [TestCase(40, 5, 444003.0, 658008.0)]
    [TestCase(40, 6, 2556389.0, 3838380.0)]
    [TestCase(60, 5, 3525478.0, 5461512.0)]
    [TestCase(60, 6, 33228879.0, 50063860.0)]
    public void RealisticOverlappingUnionsMatchIndependentAllocationCounts(
        int n,
        int h,
        double numerator,
        double denominator)
    {
        CategoryBase a = new("A"), b = new("B"), c = new("C"), d = new("D");
        List<Card> deck =
        [
            new([a], 9), new([b], 6), new([a, b], 3), new([b, c], 4),
            new([c], 3), new([d], 2), new([], n - 27)
        ];
        List<Combo> combos =
        [
            new([new(a, 1, 2), new(d, 0, 0)]),
            new([new(b, 2, h), new(c, 1, h)]),
            new([new(a, 0, 0), new(c, 2, 3)]),
            new([new(b, 0, 0), new(c, 1, 2)])
        ];
        // Independent enumeration of seven row-count tuples summing to h, with
        // backtracking physical-slot assignment and product-of-binomial weights.
        double expected = numerator / denominator;
        Assert.That(_probabilityCalculator.CalculateProbabilityForCombos(deck, combos, h),
            Is.EqualTo(expected).Within(expected * Tolerance));
    }

    [Test]
    public void ThirtyRepeatedRareEventsRemainNonzero()
    {
        CategoryBase a = new("A");
        List<Card> deck = [new([a], 30), new([], 30)];
        List<Combo> combos = [.. Enumerable.Range(0, 30).Select(_ => new Combo([new(a, 30, 30)]))];
        double expected = 1.0 / 118264581564861424.0; // Re-evaluated with Wolfram: 1/Binomial[60,30].
        ProbabilityCalculationResult result = _probabilityCalculator.CalculateProbabilityResults(deck, combos, 30);
        Assert.That(result.TotalProbability, Is.EqualTo(expected).Within(expected * Tolerance));
        Assert.That(result.ComboProbabilities.All(c => Math.Abs(c.Probability / expected - 1) < Tolerance), Is.True);
    }

    [TestCase(1100, 550)]
    [TestCase(2000, 600)]
    [TestCase(int.MaxValue, 51)]
    public void LargeExactCountsRemainFiniteAcrossTotalStandaloneAndGroups(int population, int handSize)
    {
        CategoryBase a = new("A");
        List<Card> deck = [new([a]), new([], population - 1)];
        List<Combo> combos =
        [
            new([new(a, 1, 1)], "Present", groupId: "present"),
            new([new(a, 0, 0)], "Absent", groupId: "absent")
        ];
        // A distinguished physical copy is in h/n hands. Python independently
        // evaluates Fraction(comb(n-1,h-1), comb(n,h)); the complement is (n-h)/n.
        double expected = (double)handSize / population;
        ProbabilityCalculationResult result = _probabilityCalculator.CalculateProbabilityResults(deck,
            combos,
            handSize,
            [new("present", "Present"), new("absent", "Absent")]);
        Assert.Multiple(() =>
        {
            Assert.That(result.TotalProbability, Is.EqualTo(1));
            Assert.That(result.ComboProbabilities.Select(c => c.Probability),
                Is.EqualTo(new[] { expected, 1 - expected }).Within(Tolerance));
            Assert.That(result.GroupProbabilities!.Select(g => g.Probability),
                Is.EqualTo(new[] { expected, 1 - expected }).Within(Tolerance));
            Assert.That(_probabilityCalculator.CalculateProbabilityForCombos(deck, [combos[0]], handSize),
                Is.EqualTo(expected).Within(Tolerance));
        });
    }

    [TestCase(1030, 3.496941992245984e-309)]
    [TestCase(1040, 3.431511947555e-312)]
    [TestCase(1078, 3 * double.Epsilon)]
    [TestCase(1080, double.Epsilon)]
    [TestCase(1100, 0d)]
    public void RareHandsRoundToRepresentableSubnormalProbabilities(int population, double expected)
    {
        CategoryBase a = new("A");
        int hand = population / 2;
        // Exactly one physical hand contains every designated copy. Independent
        // Python 3: float(Fraction(1, math.comb(n, n//2))). Exact double assertions
        // distinguish the smallest subnormal from zero and legitimate underflow.
        double actual = _probabilityCalculator.CalculateProbabilityForCombos(
            [new([a], hand), new([], hand)],
            [new([new(a, hand, hand)])],
            hand);
        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public void FiniteNumeratorOverOverflowingDenominatorPreservesANormalProbability()
    {
        CategoryBase a = new("A");
        // All 550 drawn copies must come from the 1000 designated copies.
        // Python: float(Fraction(math.comb(1000,550), math.comb(1100,550))).
        const double expected = 5.555993916883148e-33;
        double actual = _probabilityCalculator.CalculateProbabilityForCombos(
            [new([a], 1000), new([], 100)],
            [new([new(a, 550, 550)])],
            550);
        Assert.That(actual, Is.EqualTo(expected).Within(expected * Tolerance));
    }
}
