using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public class PostDrawExchangeableTest {
    private static IEnumerable<TestCaseData> Cases() {
        foreach (int draw in new[] { 2, 3 }) {
            foreach (int filler in new[] { 0, 1, 2 }) {
                foreach (int effects in new[] { 1, 2, 3 }) {
                    foreach (int hand in new[] { 0, 1, 2, 4 }) {
                        yield return new(draw, filler, effects, hand);
                    }
                }
            }
        }
    }

    [TestCaseSource(nameof(Cases))]
    public void ExchangeableScheduleAndPriorityTruncationMatchPhysicalOracle(int draw, int filler, int effects, int hand) {
        CategoryBase a = new("A");
        CategoryBase b = new("B");
        List<Card> deck = [new([a, b], 2, "Same", id: "a"), new([b], 1, "Same", id: "b"),
            new([], filler, id: "f"), new([], effects, id: "g", drawCount: draw,
                drawOncePerTurn: false, postDrawDiscardCount: draw - 1)];
        List<Combo> combos = [new([new(a, 1, 0, RequirementMaximumMode.HandSize), new(b, 1, 0, RequirementMaximumMode.HandSize)], groupId: "g"),
            new([], cards: [new("a", 1, 1)], groupId: "g"), new([new(b, 0, 1)]),
            new([], cards: [new("g", 0, 0)]),
            new([], alternativeGroups: [new([ComboAlternative.For(new ComboCategory(a, 1, 1)), ComboAlternative.For(new ComboCard("b", 1, 1))])])];
        AssertParity(deck, hand, combos, [new("g", "Group"), new("empty", "Empty")]);
    }

    [TestCase(false), TestCase(true)]
    public void MaximumOnlySelectorsPreserveUnusableEffectVersusFillerTie(bool effectFirst) {
        CategoryBase role = new("Role");
        CategoryBase excluded = new("Excluded");
        List<Card> deck = [new([role], 2, id: "r"),
            new(effectFirst ? [] : [excluded], 1, id: "f"),
            new(effectFirst ? [excluded] : [], 2, id: "g", drawCount: 3,
                drawOncePerTurn: false, postDrawDiscardCount: 2)];
        List<Combo> combos = [new([new(role, 1, 2), new(excluded, 0, 0)])];
        AssertParity(deck, 2, combos, []);
    }

    private static void AssertParity(List<Card> deck, int hand, List<Combo> combos, List<ComboGroup> groups) =>
        PreDrawCostsTest.AssertParity(deck, hand, combos, groups);
    [Test]
    public async Task RealisticThreeGracefulCompletesUnderStandardAndBothTogglesPreserveSettings() {
        SessionState session = await RealisticGracefulFixture.Load();
        Card graceful = session.Cards.Single(card => card.ExternalCardId == 79571449);
        Assert.Multiple(() => {
            Assert.That(graceful.DrawCount, Is.EqualTo(3));
            Assert.That(graceful.DrawOncePerTurn, Is.False);
            Assert.That(graceful.PostDrawDiscardCount, Is.EqualTo(2));
            Assert.That(graceful.Categories.Any(category => category.MetadataKey == "kind:spell"), Is.True);
            Assert.That(session.Combos, Has.Count.EqualTo(8));
            Assert.That(session.ComboGroups, Has.Count.EqualTo(2));
        });
        ProbabilityCalculatorService service = new();
        ProbabilityCalculationResult exact = service.CalculateProbabilityResults(session.Cards, session.Combos, session.HandSize,
            session.ComboGroups, CalculationWorkPolicy.Interactive, new(Mode: CalculationModel.Exact));
        Assert.That(exact.Contract!.Accuracy, Is.EqualTo(CalculationAccuracy.Exact));
        foreach ((bool global, bool conditional) in new[] { (true, true), (true, false), (false, true), (false, false) }) {
            List<Card> modeled = conditional ? session.Cards : session.Cards.Select(card => card.WithPostDrawDiscardCount(0)).ToList();
            CalculationSnapshot snapshot = CalculationSnapshot.Capture(modeled, session.Combos, session.HandSize,
                session.ComboGroups, CalculationWorkPolicy.Interactive, global, options: new());
            ProbabilityCalculationResult result = CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json));
            List<Card> expectedDeck = global ? session.Cards.Select(card => card.WithPostDrawDiscardCount(0)).ToList()
                : session.Cards.Select(card => card.WithDrawCount(null)).ToList();
            ProbabilityCalculationResult expected = global && conditional
                ? exact
                : service.CalculateProbabilityResults(expectedDeck, session.Combos, session.HandSize, session.ComboGroups, CalculationWorkPolicy.Interactive);
            Assert.Multiple(() => {
                Assert.That(result.TotalProbability, Is.EqualTo(expected.TotalProbability));
                Assert.That(result.ComboProbabilities.Select(row => row.Probability), Is.EqualTo(expected.ComboProbabilities.Select(row => row.Probability)));
                Assert.That(result.GroupProbabilities.Select(row => row.Probability), Is.EqualTo(expected.GroupProbabilities.Select(row => row.Probability)));
                Assert.That(graceful.PostDrawDiscardCount, Is.EqualTo(2));
            });
        }
    }
}
