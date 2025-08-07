using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public class ProbabilityCalculatorServiceTest {
    private const double Tolerance = 1e-12;
    private ProbabilityCalculatorService _probabilityCalculator;

    [SetUp]
    public void Setup() {
        _probabilityCalculator = new ProbabilityCalculatorService();
    }

    [Test]
    public void AllCategoriesMaxZero() {
        var categoryA = new CategoryBase("A");
        var deck = new List<Card> {
            new([categoryA], 2),
            new([], 2)
        };

        var combo = new Combo([new ComboCategory(categoryA, 0, 0)]);
        var probability = _probabilityCalculator.CalculateProbabilityForCombos(deck, [combo], 2);

        Assert.That(probability, Is.EqualTo(1.0 / 6.0).Within(Tolerance));
    }

    [Test]
    public void MultipleRangedCategories_ZeroToThree_ShouldBe100Percent() {
        const int handSize = 5;

        var categoryA = new CategoryBase("A");
        var categoryB = new CategoryBase("B");
        var categoryC = new CategoryBase("C");

        var deck = new List<Card> {
            new([categoryA, categoryB], 3),
            new([categoryB, categoryC], 3),
            new([categoryA, categoryC], 3),
            new([categoryA], 3),
            new([categoryB], 3),
            new([categoryC], 3),
            new([categoryA, categoryB, categoryC], 2),
            new([], 20)
        };

        var combo = new Combo([
            new ComboCategory(categoryA, 0, handSize),
            new ComboCategory(categoryB, 0, handSize),
            new ComboCategory(categoryC, 0, handSize)
        ]);

        var probability = _probabilityCalculator.CalculateProbabilityForCombos(deck, [combo], handSize);
        Assert.That(probability, Is.EqualTo(1.0).Within(Tolerance));
    }

    [Test]
    public void CalculateProbabilityForCombos_SubsetScenario_EqualsSingleComboProbability() {
        var categoryA = new CategoryBase("A");
        var categoryB = new CategoryBase("B");

        var deck = new List<Card> {
            new([categoryA]),
            new([categoryA]),
            new([categoryA]),
            new([categoryB]),
            new([categoryB]),
            new([categoryB])
        };

        const int handSize = 2;

        var comboA = new Combo([new ComboCategory(categoryA, 1, 1)]);
        var comboAb = new Combo([
            new ComboCategory(categoryA, 1, 1),
            new ComboCategory(categoryB, 1, 1)
        ]);

        var probA = _probabilityCalculator.CalculateProbabilityForCombos(deck, [comboA], handSize);
        var probBoth = _probabilityCalculator.CalculateProbabilityForCombos(deck, [comboA, comboAb], handSize);

        Assert.That(probBoth, Is.EqualTo(probA).Within(Tolerance));
    }

    [Test]
    public void CalculateProbabilityForCombos_ExampleScenario_CalculatedCorrectly() {
        var categoryA = new CategoryBase("A");
        var categoryB = new CategoryBase("B");
        var categoryC = new CategoryBase("C");

        var cards = new List<Card> {
            new([categoryA]),
            new([categoryB]),
            new([categoryC]),
            new([])
        };

        var combo1 = new Combo([new ComboCategory(categoryA, 1, 1)]);
        var combo2 = new Combo([
            new ComboCategory(categoryB, 1, 1),
            new ComboCategory(categoryC, 1, 1)
        ]);

        var probability = _probabilityCalculator.CalculateProbabilityForCombos(cards, [combo1, combo2], 2);
        Assert.That(probability, Is.EqualTo(0.5 + 1.0 / 6.0).Within(Tolerance));
    }

    [Test]
    public void ExactRangeRequirements_CalculateCorrectProbability() {
        var starterCat = new CategoryBase("starter");
        var extenderCat = new CategoryBase("extender");

        var deck = new List<Card> {
            new([starterCat], 2),
            new([extenderCat]),
            new([starterCat, extenderCat])
        };

        var combo = new Combo([
            new ComboCategory(starterCat, 1, 1),
            new ComboCategory(extenderCat, 1, 1)
        ]);

        var probability = _probabilityCalculator.CalculateProbabilityForCombos(deck, [combo], 2);
        Assert.That(probability, Is.EqualTo(5.0 / 6.0).Within(Tolerance));
    }

    [Test]
    public void MinGreaterThanOneRequirements_CalculatedCorrectly() {
        const int handSize = 3;

        var starterCat = new CategoryBase("starter");

        var deck = new List<Card> {
            new([starterCat], 3),
            new([], 2)
        };

        var combo = new Combo([new ComboCategory(starterCat, 2, handSize)]);

        var probability = _probabilityCalculator.CalculateProbabilityForCombos(deck, [combo], handSize);
        Assert.That(probability, Is.EqualTo(7.0 / 10.0).Within(Tolerance));
    }

    [Test]
    public void OverlapThreeCategories_CalculatedCorrectly() {
        const int handSize = 3;

        var starterCat = new CategoryBase("starter");
        var extenderCat = new CategoryBase("extender");
        var comboCat = new CategoryBase("combo");

        var deck = new List<Card> {
            new([starterCat]),
            new([extenderCat]),
            new([comboCat]),
            new([starterCat, extenderCat, comboCat]),
            new([])
        };

        var combo = new Combo([
            new ComboCategory(starterCat, 1, handSize),
            new ComboCategory(extenderCat, 1, handSize),
            new ComboCategory(comboCat, 1, handSize)
        ]);

        var probability = _probabilityCalculator.CalculateProbabilityForCombos(deck, [combo], handSize);
        Assert.That(probability, Is.EqualTo(7.0 / 10.0).Within(Tolerance));
    }
}
