using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public class ProbabilityCalculatorServiceTest {
    private const double Tolerance = 1e-12;

    [Test]
    public void AllCategoriesMaxZero() {
        var categoryA = new CategoryBase("A");
        var deck = new List<Card> {
            new([categoryA], 2),
            new([], 2)
        };

        var categories = new[] { new Category("A", 0, 0) };

        var probability = ProbabilityCalculatorService.CalculateProbabilityForCategories(deck, categories, 2);

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

        var categories = new[] {
            new Category("A", 0, handSize),
            new Category("B", 0, handSize),
            new Category("C", 0, handSize)
        };

        var probability = ProbabilityCalculatorService.CalculateProbabilityForCategories(deck, categories, handSize);
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

        var categories = new[] { new Category("A", 1, 1) };

        var singleProb = ProbabilityCalculatorService.CalculateProbabilityForCategories(deck, categories, handSize);
        var comboProb = ProbabilityCalculatorService.CalculateProbabilityForCombos(deck, [comboA, comboAb], handSize);

        Assert.That(comboProb, Is.EqualTo(singleProb).Within(Tolerance));
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

        var probability = ProbabilityCalculatorService.CalculateProbabilityForCombos(cards, [combo1, combo2], 2);
        Assert.That(probability, Is.EqualTo(0.5 + 1.0 / 6.0).Within(Tolerance));
    }

    [Test]
    public void ExactRangeRequirements_CalculateCorrectProbability() {
        var starter = new CategoryBase("starter");
        var extender = new CategoryBase("extender");

        var deck = new List<Card> {
            new([starter], 2),
            new([extender]),
            new([starter, extender])
        };

        var requirements = new[] {
            new Category("starter", 1, 1),
            new Category("extender", 1, 1)
        };

        var probability = ProbabilityCalculatorService.CalculateProbabilityForCategories(deck, requirements, 2);
        Assert.That(probability, Is.EqualTo(5.0 / 6.0).Within(Tolerance));
    }

    [Test]
    public void MinGreaterThanOneRequirements_CalculatedCorrectly() {
        const int handSize = 3;

        var starter = new CategoryBase("starter");

        var deck = new List<Card> {
            new([starter], 3),
            new([], 2)
        };

        var requirements = new[] { new Category("starter", 2, handSize) };

        var probability = ProbabilityCalculatorService.CalculateProbabilityForCategories(deck, requirements, handSize);
        Assert.That(probability, Is.EqualTo(7.0 / 10.0).Within(Tolerance));
    }

    [Test]
    public void OverlapThreeCategories_CalculatedCorrectly() {
        const int handSize = 3;

        var starter = new CategoryBase("starter");
        var extender = new CategoryBase("extender");
        var combo = new CategoryBase("combo");

        var deck = new List<Card> {
            new([starter]),
            new([extender]),
            new([combo]),
            new([starter, extender, combo]),
            new([])
        };

        var categories = new[] {
            new Category("starter", 1, handSize),
            new Category("extender", 1, handSize),
            new Category("combo", 1, handSize)
        };

        var probability = ProbabilityCalculatorService.CalculateProbabilityForCategories(deck, categories, handSize);
        Assert.That(probability, Is.EqualTo(7.0 / 10.0).Within(Tolerance));
    }
}
