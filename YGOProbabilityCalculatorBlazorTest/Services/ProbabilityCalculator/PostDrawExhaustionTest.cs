using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public class PostDrawExhaustionTest {
    private static readonly CategoryBase A = new("A");
    private static readonly CategoryBase B = new("B");
    private static readonly CategoryBase Dark = new("Dark", CategorySource.Metadata, "attribute:dark");

    [Test]
    public void PartialGracefulDrawKeepsActualCardsBeforeUnrestrictedDiscard() {
        Card source = new([A], id: "source", drawCount: 3, drawOncePerTurn: false, postDrawDiscardCount: 2);
        List<Card> deck = [source, new([A, B], id: "first"), new([B], id: "second")];
        List<Combo> combos = Requirements();
        List<ComboGroup> groups = Groups();

        DrawExhaustionAssertions.AssertAdvancedMatchesOracle(deck, 1, combos, groups);
        Assert.That(DrawSequenceOracle.Resolve([source, deck[1], deck[2]], 1, combos), Is.Empty);
    }

    [Test]
    public void PartialFilteredDrawRemovesEligibleCardFromTheActualDraws() {
        Card source = new([A], id: "source", drawCount: 3, drawOncePerTurn: false,
            postDrawDiscardCount: 1, postDrawSelector: CardSelector.For(Dark));
        List<Card> deck = [source, new([Dark, B], id: "eligible"), new([A], id: "kept")];

        DrawExhaustionAssertions.AssertAdvancedMatchesOracle(deck, 1, Requirements(), Groups());
        Assert.That(DrawSequenceOracle.Resolve([source, deck[1], deck[2]], 1, Requirements())!
            .Select(card => card.Id), Is.EqualTo(new[] { "kept" }));
    }

    [Test]
    public void PartialFilteredRemovalStillFailsWhenNoEligibleCardWasDrawn() {
        Card source = new([A], id: "source", drawCount: 3, drawOncePerTurn: false,
            postDrawDiscardCount: 1, postDrawSelector: CardSelector.For(Dark));
        List<Card> deck = [source, new([A], id: "first"), new([B], id: "second")];
        DrawSequenceOracle.Counts oracle = DrawSequenceOracle.Enumerate(deck, 1, [new([])], []);

        Assert.That(oracle.Failed, Is.EqualTo(new BigInteger(2)));
        DrawExhaustionAssertions.AssertAdvancedMatchesOracle(deck, 1, [new([])], []);
    }

    [Test]
    public void PartialFilteredRemovalCanRemovePendingDrawCardBeforeItActivates() {
        Card source = new([A], id: "source", drawCount: 3, drawOncePerTurn: false,
            postDrawDiscardCount: 1, postDrawSelector: CardSelector.For(Dark));
        Card pending = new([Dark], id: "pending", drawCount: 1, drawOncePerTurn: false,
            postDrawDiscardCount: 1, postDrawSelector: CardSelector.For(Dark));
        Card reward = new([B], id: "reward");
        List<Card> deck = [source, pending, reward];

        DrawExhaustionAssertions.AssertAdvancedMatchesOracle(deck, 1, Requirements(), Groups());
        Assert.That(DrawSequenceOracle.Resolve(deck, 1, Requirements())!.Select(card => card.Id), Is.EqualTo(new[] { "reward" }));
    }

    private static List<Combo> Requirements() => [
        new([new(A, 1, 0, RequirementMaximumMode.HandSize), new(B, 1, 0, RequirementMaximumMode.HandSize)], groupId: "routes"),
        new([new(A, 1, 1)], cards: [new("eligible", 1, 2)], groupId: "routes"),
        new([new(A, 0, 0)]),
        new([new(B, 0, 1)]),
        new([], cards: [new("source", 1, 2)])
    ];

    private static List<ComboGroup> Groups() => [new("routes", "Routes"), new("empty", "Empty")];
}
