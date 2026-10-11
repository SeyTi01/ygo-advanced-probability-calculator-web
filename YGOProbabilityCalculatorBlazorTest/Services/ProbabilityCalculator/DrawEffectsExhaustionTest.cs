using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public class DrawEffectsExhaustionTest {
    private static readonly CategoryBase A = new("A");
    private static readonly CategoryBase B = new("B");

    private static IEnumerable<TestCaseData> SingleEffectBoundaries() {
        for (int draw = 1; draw <= 3; draw++) {
            for (int remaining = 0; remaining <= draw; remaining++) {
                List<Card> deck = [new([], id: "source", drawCount: draw, drawOncePerTurn: false)];
                if (remaining > 0) {
                    deck.Add(new([], remaining, id: "reward"));
                }

                yield return new TestCaseData(deck, draw, remaining)
                    .SetName($"Draw{draw}_With{remaining}CardsRemaining");
            }
        }
    }

    [TestCaseSource(nameof(SingleEffectBoundaries))]
    public void DrawActivatesOnlyWhenTheFullBatchFits(List<Card> deck, int draw, int remaining) {
        List<Card> order = deck.SelectMany(card => Enumerable.Repeat(card, card.Copies)).ToList();
        List<Card> finalHand = DrawSequenceOracle.Resolve(order, 1, [])!;

        if (remaining < draw) {
            Assert.That(finalHand.Select(card => card.Id), Is.EqualTo(new[] { "source" }));
        }
        else {
            Assert.That(finalHand.Count, Is.EqualTo(draw));
            Assert.That(finalHand.Any(card => card.Id == "source"), Is.False);
        }

        AssertFixedModeParity(deck, 1);
    }

    [TestCase(2, 1)]
    [TestCase(3, 2)]
    public void AnUnavailableEarlierCandidateDoesNotBlockALaterSmallerBatch(int largeDraw, int remaining) {
        List<Card> deck = [
            new([], name: "Same name", id: "large", drawCount: largeDraw),
            new([], name: "Same name", id: "small", drawCount: remaining),
            new([], remaining, id: "reward")
        ];

        List<Card> finalHand = DrawSequenceOracle.Resolve(PhysicalDeck(deck), 2, [])!;
        string[] expected = largeDraw == 2 ? ["large", "reward"] : ["large", "reward", "reward"];
        Assert.That(finalHand.Select(card => card.Id), Is.EquivalentTo(expected));
        AssertFixedModeParity(deck, 2);
    }

    [Test]
    public void AnEarlierSmallBatchCanMakeALaterLargeBatchUnavailable() {
        Card small = new([], id: "small", drawCount: 1, drawOncePerTurn: false);
        Card large = new([], id: "large", drawCount: 2, drawOncePerTurn: false);
        Card first = new([], id: "first");
        Card second = new([], id: "second");

        List<Card> smallFirst = DrawSequenceOracle.Resolve([small, large, first, second], 2, [])!;
        List<Card> largeFirst = DrawSequenceOracle.Resolve([large, small, first, second], 2, [])!;

        Assert.That(smallFirst.Select(card => card.Id), Is.EquivalentTo(new[] { "large", "first" }));
        Assert.That(largeFirst.Select(card => card.Id), Is.EquivalentTo(new[] { "small", "first", "second" }));
        AssertFixedModeParity([small, large, first, second], 2);
    }

    [Test]
    public void ANewlyDrawnEffectRemainsInHandWhenItsBatchNoLongerFits() {
        Card source = new([], id: "source", drawCount: 2, drawOncePerTurn: false);
        Card nested = new([], id: "nested", drawCount: 2, drawOncePerTurn: false);
        Card reward = new([], id: "reward");

        List<Card> finalHand = DrawSequenceOracle.Resolve([source, nested, reward], 1, [])!;
        Assert.That(finalHand.Select(card => card.Id), Is.EquivalentTo(new[] { "nested", "reward" }));
        AssertFixedModeParity([source, nested, reward], 1);
    }

    [Test]
    public void SeveralPermanentlyUnavailableCandidatesTerminateAndStayInHand() {
        List<Card> deck = [
            new([], id: "first", drawCount: 3),
            new([], id: "second", drawCount: 3),
            new([], id: "third", drawCount: 3),
            new([], 2, id: "reward")
        ];

        List<Card> finalHand = DrawSequenceOracle.Resolve(PhysicalDeck(deck), 3, [])!;
        Assert.That(finalHand.Select(card => card.Id), Is.EquivalentTo(new[] { "first", "second", "third" }));
        AssertFixedModeParity(deck, 3);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DuplicateCopiesOnlyConsumeSourcesWhoseFullBatchFits(bool oncePerTurn) {
        List<Card> deck = [
            new([], 2, name: "Duplicate", id: "duplicate", drawCount: 2, drawOncePerTurn: oncePerTurn),
            new([], 2, id: "reward")
        ];

        List<Card> finalHand = DrawSequenceOracle.Resolve(deck.SelectMany(card => Enumerable.Repeat(card, card.Copies)).ToArray(), 2, [])!;
        Assert.That(finalHand.Select(card => card.Id), Is.EquivalentTo(new[] { "duplicate", "reward", "reward" }));
        AssertFixedModeParity(deck, 2);
    }

    [Test]
    public void UnavailableOptEntryDoesNotSuppressASeparateSameNameEntry() {
        List<Card> deck = [
            new([], name: "Same name", id: "large", drawCount: 2),
            new([], name: "Same name", id: "small", drawCount: 1),
            new([], id: "reward")
        ];

        List<Card> finalHand = DrawSequenceOracle.Resolve(deck, 2, [])!;
        Assert.That(finalHand.Select(card => card.Id), Is.EquivalentTo(new[] { "large", "reward" }));
        AssertFixedModeParity(deck, 2);
    }

    private static void AssertFixedModeParity(List<Card> deck, int handSize) {
        DrawExhaustionAssertions.AssertBasicAndAdvancedMatchOracle(deck, handSize);
        DrawExhaustionAssertions.AssertBasicAndAdvancedMatchOracle(deck.AsEnumerable().Reverse().ToList(), handSize);
    }

    private static Card[] PhysicalDeck(IEnumerable<Card> deck) =>
        deck.SelectMany(card => Enumerable.Repeat(card, card.Copies)).ToArray();
}
