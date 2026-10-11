using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public class DrawEffectsExhaustionTest {
    private static readonly CategoryBase A = new("A");
    private static readonly CategoryBase B = new("B");

    private static IEnumerable<TestCaseData> ExhaustionDecks() {
        foreach ((int draw, int remaining) in new[] { (1, 0), (2, 0), (2, 1), (3, 1), (3, 2) }) {
            List<Card> deck = [new([A], id: "source", drawCount: draw, drawOncePerTurn: false)];
            if (remaining > 0) {
                deck.Add(new([A, B], remaining, id: "reward"));
            }
            yield return new TestCaseData(deck, 1).SetName($"FixedDraw{draw}_With{remaining}CardsRemaining");
        }

        yield return new TestCaseData(new List<Card> {
            new([A], id: "source", drawCount: 3, drawOncePerTurn: false),
            new([B], id: "nested", drawCount: 1, drawOncePerTurn: false),
            new([A, B], id: "reward")
        }, 1).SetName("PartialBatchQueuesAnotherEffect");

        yield return new TestCaseData(new List<Card> {
            new([A], id: "source", drawCount: 2, drawOncePerTurn: false),
            new([B], id: "reward"),
            new([A, B], id: "last", drawCount: 1, drawOncePerTurn: false)
        }, 1).SetName("LastDeckCardQueuesAnEffect");

        yield return new TestCaseData(new List<Card> {
            new([A], id: "first", drawCount: 1, drawOncePerTurn: false),
            new([B], id: "second", drawCount: 1, drawOncePerTurn: false),
            new([A, B], id: "third", drawCount: 1, drawOncePerTurn: false),
            new([], id: "reward")
        }, 3).SetName("MultipleQueuedEffectsResolveAtEmptyDeck");

        yield return new TestCaseData(new List<Card> {
            new([A], id: "source", drawCount: 3),
            new([B], id: "nested", drawCount: 2),
            new([A, B], id: "reward")
        }, 1).SetName("OncePerTurnRecursiveEffectsResolveAtEmptyDeck");

        yield return new TestCaseData(new List<Card> {
            new([A], id: "source", drawCount: 3, drawOncePerTurn: false),
            new([B], id: "nested", drawCount: 2, drawOncePerTurn: false),
            new([A, B], id: "reward")
        }, 1).SetName("UnrestrictedRecursiveEffectsResolveAtEmptyDeck");

        foreach (bool once in new[] { false, true }) {
            yield return new TestCaseData(new List<Card> {
                new([A], 2, id: "duplicate", drawCount: 2, drawOncePerTurn: once),
                new([B], 2, id: "reward")
            }, 1).SetName($"DuplicateEffectCopiesResolveAtEmptyDeck_OncePerTurn{once}");
        }
    }

    [TestCaseSource(nameof(ExhaustionDecks))]
    public void BasicAndAdvancedFixedDrawsMatchPhysicalOracle(List<Card> deck, int handSize) {
        DrawExhaustionAssertions.AssertBasicAndAdvancedMatchOracle(deck, handSize);
    }
}
