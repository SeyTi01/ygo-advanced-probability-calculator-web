using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public class SmallDeckOracleTest {
    private static readonly CategoryBase A = new("A");
    private static readonly CategoryBase B = new("B");
    private static readonly CategoryBase C = new("C");

    private static IEnumerable<TestCaseData> Cases() {
        List<Card> deck = [new([A], 2), new([B]), new([A, B]), new([B, C]), new([], 2)];
        yield return Case("positive maximum", deck, [new([new(A, 1, 1)])], 2);
        yield return Case("zero maximum", deck, [new([new(A, 0, 0)])], 2);
        yield return Case("overlapping categories", deck, [new([new(A, 1, 2), new(B, 1, 2)])], 3);
        yield return Case("overlapping combos", deck, [new([new(A, 1, 3)]), new([new(B, 1, 3)])], 3);
        yield return Case("subset combos", deck, [new([new(A, 1, 2)]), new([new(A, 1, 2), new(B, 1, 2)])], 2);
        yield return Case("disjoint ranges", deck, [new([new(A, 0, 0)]), new([new(A, 2, 2)])], 2);
        yield return Case("duplicate combos", deck, [
            new([new(A, 1, 2)], "Same name"),
            new([new(A, 1, 2)], "Same name")
        ], 2);
        yield return Case("minimum above available copies", deck, [new([new(C, 2, 3)])], 3);
        yield return Case("unconstrained combo", deck, [new([])], 3);
        yield return Case("no combos", deck, [], 2);
        yield return Case("whole deck", deck, [new([new(A, 3, 3), new(B, 3, 3)])], 7);
    }

    private static TestCaseData Case(string name, List<Card> deck, List<Combo> combos, int size) =>
        new TestCaseData(deck, combos, size).SetName($"Oracle: {name}");

    [TestCaseSource(nameof(Cases))]
    public void EngineMatchesEveryPhysicalHand(List<Card> deck, List<Combo> combos, int handSize) {
        var expected = EnumerateProbability(deck, combos, handSize);
        var actual = new ProbabilityCalculatorService().CalculateProbabilityForCombos(deck, combos, handSize);
        Assert.That(actual, Is.EqualTo(expected).Within(1e-12));
    }

    [TestCaseSource(nameof(Cases))]
    public void TotalAndStandaloneResultsMatchEveryPhysicalHand(List<Card> deck, List<Combo> combos, int handSize) {
        var expectedTotal = EnumerateProbability(deck, combos, handSize);
        var result = new ProbabilityCalculatorService().CalculateProbabilityResults(deck, combos, handSize);

        Assert.That(result.TotalProbability, Is.EqualTo(expectedTotal).Within(1e-12));
        Assert.That(result.ComboProbabilities, Has.Count.EqualTo(combos.Count));
        for (var index = 0; index < combos.Count; index++) {
            var comboResult = result.ComboProbabilities[index];
            Assert.That(comboResult.ComboIndex, Is.EqualTo(index));
            Assert.That(comboResult.ComboName, Is.EqualTo(combos[index].Name));
            Assert.That(comboResult.Probability,
                Is.EqualTo(EnumerateProbability(deck, [combos[index]], handSize)).Within(1e-12),
                $"Standalone result for combo at index {index}");
        }
    }

    [Test]
    public void OverlappingComboResultsRemainSeparateFromTheirUnion() {
        var deck = new List<Card> {
            new([A], 2), new([B], 2), new([A, B]), new([], 2)
        };
        var combos = new List<Combo> {
            new([new(A, 1, 2)], "Duplicate"),
            new([new(B, 1, 2)], "Duplicate")
        };
        const int handSize = 2;

        var result = new ProbabilityCalculatorService().CalculateProbabilityResults(deck, combos, handSize);
        var expectedTotal = EnumerateProbability(deck, combos, handSize);
        var expectedStandalone = combos
            .Select(combo => EnumerateProbability(deck, [combo], handSize))
            .ToArray();

        Assert.That(result.TotalProbability, Is.EqualTo(expectedTotal).Within(1e-12));
        Assert.That(result.ComboProbabilities.Select(combo => combo.Probability),
            Is.EqualTo(expectedStandalone).Within(1e-12));
        Assert.That(result.TotalProbability, Is.LessThan(expectedStandalone.Sum()));
        Assert.That(result.ComboProbabilities.Select(combo => combo.ComboName),
            Is.EqualTo(new[] { "Duplicate", "Duplicate" }));
    }

    [Test]
    public void SeededSmallDecksMatchEnumeration() {
        var random = new Random(120925);
        CategoryBase[] categories = [A, B, C];
        for (var sample = 0; sample < 24; sample++) {
            var deck = Enumerable.Range(0, 6)
                .Select(_ => new Card(categories.Where(_ => random.Next(2) == 1))).ToList();
            var handSize = random.Next(1, 4);
            var combos = Enumerable.Range(0, random.Next(1, 4)).Select(_ => new Combo(
                categories.Where(_ => random.Next(2) == 1).Select(category => {
                    var min = random.Next(handSize + 1);
                    return new ComboCategory(category, min, random.Next(min, handSize + 1));
                }))).ToList();
            var actual = new ProbabilityCalculatorService().CalculateProbabilityForCombos(deck, combos, handSize);
            Assert.That(actual, Is.EqualTo(EnumerateProbability(deck, combos, handSize)).Within(1e-12),
                $"Seed 120925, sample {sample}, hand size {handSize}");
        }
    }

    // Each physical copy has its own position. Visit each unordered hand exactly once;
    // directly evaluate OR-of-combos / AND-of-constraints, without masks, merging,
    // binomial coefficients, inclusion-exclusion, or production helper methods.
    internal static double EnumerateProbability(List<Card> deck, List<Combo> combos, int handSize) {
        var copies = new List<Card>();
        foreach (var card in deck)
            for (var i = 0; i < card.Copies; i++) copies.Add(card);
        var hand = new List<Card>();
        var total = 0;
        var successes = 0;
        Visit(0);
        return (double)successes / total;

        void Visit(int start) {
            if (hand.Count == handSize) {
                total++;
                if (combos.Any(combo => combo.Categories.All(constraint => {
                    var count = hand.Count(card => card.Categories.Any(category =>
                        category.Name == constraint.BaseCategory.Name));
                    return count >= constraint.MinCount && count <= constraint.MaxCount;
                }))) successes++;
                return;
            }
            for (var i = start; i <= copies.Count - (handSize - hand.Count); i++) {
                hand.Add(copies[i]);
                Visit(i + 1);
                hand.RemoveAt(hand.Count - 1);
            }
        }
    }
}
