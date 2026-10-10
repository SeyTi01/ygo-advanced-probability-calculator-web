using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public class DrawEffectsTest {
    private static readonly CategoryBase A = new("A");
    private static readonly CategoryBase B = new("B");
    private static readonly CategoryBase C = new("C");

    private static IEnumerable<TestCaseData> Decks() {
        (int Copies, int Draw)[][] effects = [ [(1, 1)], [(1, 2)], [(1, 3)], [(2, 2)], [(3, 1)],
            [(1, 2), (1, 2)], [(1, 1), (2, 2)], [(1, 3), (1, 1)], [(2, 1), (2, 3)] ];
        foreach ((int Copies, int Draw)[] entries in effects) {
            List<Card> deck = [new([A, B], id: "ordinary"), new([A], id: "a"), new([C], id: "c")];
            for (int i = 0; i < entries.Length; i++) {
                deck.Add(new(i == 0 ? [A, B] : [B, C], entries[i].Copies, "Same display name", id: $"effect{i}", drawCount: entries[i].Draw));
            }

            foreach (int size in new[] { 0, 1, 2, deck.Sum(card => card.Copies) }) {
                yield return new TestCaseData(deck, size).SetName($"DrawParity_{string.Join('_', entries.Select(entry => $"{entry.Copies}x{entry.Draw}"))}_h{size}");
            }
        }

        yield return new TestCaseData(new List<Card> { new([A, B], 3, id: "effect0", drawCount: 1) }, 1).SetName("DrawParity_RetainedOnly");
        yield return new TestCaseData(new List<Card> { new([A], id: "effect0", drawCount: 2) }, 1).SetName("DrawParity_AllExhausted");
    }

    private static List<Combo> Requirements() => [
        new([], "ordinary", groupId: "g1", cards: [new("ordinary", 1, 1)]),
        new([], "retained", groupId: "g1", cards: [new("effect0", 1, 3)]),
        new([new(A, 1, 0, RequirementMaximumMode.HandSize), new(B, 1, 0, RequirementMaximumMode.HandSize)], "distinct", groupId: "g2"),
        new([new(A, 1, 1)], "fixed", groupId: "g2"),
        new([new(B, 0, 0)], "exclude", groupId: "g2"),
        new([], "exclude retained", groupId: "g3", cards: [new("effect0", 0, 0)]),
        new([new(A, 2, 0, RequirementMaximumMode.HandSize)], "any", groupId: "g3"),
        new([new(A, 1, 0, RequirementMaximumMode.HandSize)], "distinct direct", groupId: "g3", cards: [new("effect0", 1, 0, RequirementMaximumMode.HandSize)]),
        new([new(C, 0, 1)], "or", groupId: "g4", alternativeGroups: [new([
            new("Category", category: new(A, 1, 1)), new("Card", card: new("effect0", 1, 0, RequirementMaximumMode.HandSize))])]),
        new([new(A, 0, 0)], "contradiction", cards: [new("effect0", 1, 1)])
    ];

    [TestCaseSource(nameof(Decks))]
    public void ProductionMatchesEveryPhysicalPermutationExactly(List<Card> deck, int handSize) {
        List<Combo> combos = Requirements();
        List<ComboGroup> groups = [new("g1", "First"), new("g2", "Second"), new("g3", "Third"), new("g4", "Fourth"), new("empty", "Empty")];
        DrawSequenceOracle.Counts expected = DrawSequenceOracle.Enumerate(deck, handSize, combos, groups);
        WorkBudget budget = new(CalculationWorkPolicy.Interactive);
        ExactCalculationResult result = new DrawEffectCalculation(deck, handSize, budget).Calculate(combos, groups);
        ExactProbability[] actual = [result.Total, .. result.Combos, .. result.Groups];
        for (int i = 0; i < actual.Length; i++) {
            Assert.That(actual[i].Numerator * expected.Total, Is.EqualTo(expected.Wins[i] * actual[i].Denominator), $"Exact result row {i}");
        }

        List<Card> effects = deck.Where(card => card.DrawCount is not null).ToList();
        int ordinary = deck.Where(card => card.DrawCount is null).Sum(card => card.Copies);
        DrawResolution resolution = DrawEffectResolver.Resolve(ordinary, effects, handSize, new(CalculationWorkPolicy.Interactive));
        Assert.That(resolution.Exhaustion.Numerator * expected.Total, Is.EqualTo(expected.Failed * resolution.Exhaustion.Denominator));
        ExactProbability mass = resolution.Exhaustion;
        foreach ((DrawComposition scenario, ExactProbability weight) in resolution.Scenarios) {
            string key = $"{scenario.OrdinaryCount}|{string.Join(',', scenario.EffectCounts)}";
            Assert.That(weight.Numerator * expected.Total, Is.EqualTo(expected.Scenarios[key] * weight.Denominator), key);
            mass = mass.Add(weight, budget);
        }

        Assert.That(resolution.Scenarios.Count, Is.EqualTo(expected.Scenarios.Count));
        Assert.That(mass.Numerator, Is.EqualTo(mass.Denominator));
        ProbabilityCalculationResult published = new ProbabilityCalculatorService().CalculateProbabilityResults(deck, combos, handSize, groups);
        Assert.That(published.TotalProbability, Is.EqualTo((double)expected.Wins[0] / (double)expected.Total).Within(1e-14));
        Assert.That(published.ComboProbabilities.Select(row => row.ComboIndex), Is.EqualTo(Enumerable.Range(0, combos.Count)));
        Assert.That(published.GroupProbabilities!.Select(row => row.GroupId), Is.EqualTo(groups.Select(group => group.Id)));
    }

    [Test]
    public void RetainedDuplicateExampleHasTwoFifthsOrdinaryAndOneFifthEffectSuccess() {
        List<Card> deck = [new([], id: "ordinary"), new([]), new([]), new([], 2, id: "effect0", drawCount: 2)];
        List<Combo> combos = [new([], cards: [new("ordinary", 1, 1)]), new([], cards: [new("effect0", 1, 1)])];
        DrawSequenceOracle.Counts oracle = DrawSequenceOracle.Enumerate(deck, 1, combos, []);
        Assert.That(oracle.Total, Is.EqualTo(new BigInteger(120)));
        Assert.That(oracle.Wins[1], Is.EqualTo(new BigInteger(48)));
        Assert.That(oracle.Wins[2], Is.EqualTo(new BigInteger(24)));
    }

    [Test]
    public void NoEffectsRetainTheFixedHandPath() {
        List<Card> deck = [new([A, B], 2), new([A]), new([C], 2)];
        List<Combo> combos = Requirements();
        WorkBudget budget = new(CalculationWorkPolicy.Default);
        ExactCalculationResult fixedResult = new ProbabilityCalculation(deck, 2, budget).CalculateExactResults(combos, []);
        ExactCalculationResult orchestrated = new DrawEffectCalculation(deck, 2, budget).Calculate(combos, []);
        Assert.That(orchestrated.Total, Is.EqualTo(fixedResult.Total));
        Assert.That(orchestrated.Combos, Is.EqualTo(fixedResult.Combos));
    }

    [Test]
    public void OneBudgetCoversResolutionAndAllScenariosAndResults() {
        List<Card> deck = [new([A], 8), new([B], 8), new([A, B], 3, id: "effect0", drawCount: 2)];
        List<Combo> combos = Requirements();
        List<ComboGroup> groups = [new("g1", "Group")];
        WorkBudget measured = new(CalculationWorkPolicy.Interactive);
        new DrawEffectCalculation(deck, 4, measured).Calculate(combos, groups);
        CalculationWorkPolicy limited = new(measured.Spent - 1);
        List<Card> effects = [deck[2]];
        List<Card> ordinary = deck.Take(2).ToList();
        DrawResolution resolution = DrawEffectResolver.Resolve(16, effects, 4, new(limited));
        foreach (DrawComposition scenario in resolution.Scenarios.Keys) {
            List<Card> retained = scenario.EffectCounts[0] == 0 ? [] : [effects[0].WithCopies(scenario.EffectCounts[0])];
            Assert.DoesNotThrow(() => new ProbabilityCalculation(ordinary, scenario.OrdinaryCount, new WorkBudget(limited), retained)
                .CalculateExactResults(combos, groups));
        }

        ProbabilityCalculationLimitException? exception = Assert.Throws<ProbabilityCalculationLimitException>(() =>
            new DrawEffectCalculation(deck, 4, new(limited)).Calculate(combos, groups));
        Assert.That(exception!.Reason, Is.EqualTo(ProbabilityCalculationLimitReason.Work));
    }

    [Test]
    public void ResolutionStateStorageIsBoundedIndependentlyOfWorkAllowance() {
        List<Card> effects = Enumerable.Range(0, 20).Select(index => new Card([], id: $"effect{index}", drawCount: 1)).ToList();
        ProbabilityCalculationLimitException? exception = Assert.Throws<ProbabilityCalculationLimitException>(() =>
            DrawEffectResolver.Resolve(0, effects, 1, new(new(long.MaxValue))));
        Assert.That(exception!.Reason, Is.EqualTo(ProbabilityCalculationLimitReason.Storage));
    }

    [Test]
    public void ExactScenarioArithmeticPreservesRareRatiosAndZeroMass() {
        WorkBudget budget = new(CalculationWorkPolicy.Default);
        ExactProbability rare = new(1, BigInteger.One << 1074);
        ExactProbability combined = rare.Multiply(new(1, 3), budget).Add(rare.Multiply(new(2, 3), budget), budget);
        Assert.That(combined.ToDouble(budget), Is.EqualTo(double.Epsilon));
        Assert.That(ExactProbability.Zero.Multiply(rare, budget), Is.EqualTo(ExactProbability.Zero));
    }
}
