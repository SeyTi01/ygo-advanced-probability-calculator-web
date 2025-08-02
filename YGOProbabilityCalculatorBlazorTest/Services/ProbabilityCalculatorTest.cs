using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services;

namespace YGOProbabilityCalculatorBlazorTest.Services;

[TestFixture]
public class ProbabilityCalculatorTest {
    private const double Tolerance = 1e-12;

    [Test]
    public void AllCategoriesMaxZero() {
        var categoryA = new Category("A", 0, 0);
        var deck = new List<Card> {
            new([categoryA], 2),
            new([], 2)
        };

        var categories = new[] { categoryA };

        var probability = ProbabilityCalculator.CalculateProbabilityRange(deck, categories, 2);

        Assert.That(probability, Is.EqualTo(1.0 / 6.0).Within(Tolerance));
    }

    [Test]
    public void MultipleRangedCategories_ZeroToThree_ShouldBe100Percent() {
        const int handSize = 5;

        var categoryA = new Category("A", 0, handSize);
        var categoryB = new Category("B", 0, handSize);
        var categoryC = new Category("C", 0, handSize);

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

        var categories = new[] { categoryA, categoryB, categoryC };

        var probability = ProbabilityCalculator.CalculateProbabilityRange(deck, categories, handSize);
        Assert.That(probability, Is.EqualTo(1.0).Within(Tolerance));
    }

    [Test]
    public void CalculateProbabilityForCombos_SubsetScenario_EqualsSingleComboProbability() {
        var categoryA = new Category("A", 1, 1);
        var categoryB = new Category("B", 1, 1);

        var deck = new List<Card> {
            new([categoryA]),
            new([categoryA]),
            new([categoryA]),
            new([categoryB]),
            new([categoryB]),
            new([categoryB])
        };

        const int handSize = 2;

        var comboA = new Combo([categoryA]);
        var comboAb = new Combo([categoryA, categoryB]);

        var categories = new[] { categoryA };

        var singleProb = ProbabilityCalculator.CalculateProbabilityRange(deck, categories, handSize);
        var comboProb = ProbabilityCalculator.CalculateProbabilityForCombos(deck, [comboA, comboAb], handSize);

        Assert.That(comboProb, Is.EqualTo(singleProb).Within(Tolerance));
    }

    [Test]
    public void CalculateProbabilityForCombos_ExampleScenario_CalculatedCorrectly() {
        var categoryA = new Category("A", 1, 1);
        var categoryB = new Category("B", 1, 1);
        var categoryC = new Category("C", 1, 1);

        var cards = new List<Card> {
            new([categoryA]),
            new([categoryB]),
            new([categoryC]),
            new([])
        };

        var combo1 = new Combo([categoryA]);
        var combo2 = new Combo([categoryB, categoryC]);

        var probability = ProbabilityCalculator.CalculateProbabilityForCombos(cards, [combo1, combo2], 2);
        Assert.That(probability, Is.EqualTo(0.5 + 1.0 / 6.0).Within(Tolerance));
    }

    [Test]
    public void ExactRangeRequirements_CalculateCorrectProbability() {
        var starter = new Category("starter", 1, 1);
        var extender = new Category("extender", 1, 1);

        var deck = new List<Card> {
            new([starter], 2),
            new([extender]),
            new([starter, extender])
        };

        var requirements = new[] { starter, extender };

        var probability = ProbabilityCalculator.CalculateProbabilityRange(deck, requirements, 2);
        Assert.That(probability, Is.EqualTo(5.0 / 6.0).Within(Tolerance));
    }

    [Test]
    public void MinGreaterThanOneRequirements_CalculatedCorrectly() {
        const int handSize = 3;

        var starter = new Category("starter", 2, handSize);

        var deck = new List<Card> {
            new([starter], 3),
            new([], 2)
        };

        var requirements = new[] { starter };

        var probability = ProbabilityCalculator.CalculateProbabilityRange(deck, requirements, handSize);
        Assert.That(probability, Is.EqualTo(7.0 / 10.0).Within(Tolerance));
    }

    [Test]
    public void OverlapThreeCategories_CalculatedCorrectly() {
        const int handSize = 3;

        var starter = new Category("starter", 1, handSize);
        var extender = new Category("extender", 1, handSize);
        var combo = new Category("combo", 1, handSize);

        var deck = new List<Card> {
            new([starter]),
            new([extender]),
            new([combo]),
            new([starter, extender, combo]),
            new([])
        };

        var categories = new[] { starter, extender, combo };

        var probability = ProbabilityCalculator.CalculateProbabilityRange(deck, categories, handSize);
        Assert.That(probability, Is.EqualTo(7.0 / 10.0).Within(Tolerance));
    }
}
