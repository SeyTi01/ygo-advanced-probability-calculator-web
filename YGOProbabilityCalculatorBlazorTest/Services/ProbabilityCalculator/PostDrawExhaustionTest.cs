using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public class PostDrawExhaustionTest {
    private static readonly CategoryBase A = new("A");
    private static readonly CategoryBase B = new("B");
    private static readonly CategoryBase Dark = new("Dark", CategorySource.Metadata, "attribute:dark");
    private static readonly CategoryBase Role = new("Role");

    [Test]
    public void InsufficientFullBatchRetainsSourceWithoutPostDrawRemovalOrFailure() {
        Card source = new([], id: "source", drawCount: 3, drawOncePerTurn: false, postDrawDiscardCount: 2);
        List<Card> deck = [source, new([], 2, id: "reward")];
        DrawSequenceOracle.Counts oracle = DrawSequenceOracle.Enumerate(deck, 1, [new([])], []);

        Assert.That(oracle.Total, Is.EqualTo(new BigInteger(6)));
        Assert.That(oracle.Failed, Is.EqualTo(BigInteger.Zero));
        Assert.That(DrawSequenceOracle.Resolve([source, deck[1], deck[1]], 1, [])!.Select(card => card.Id),
            Is.EqualTo(new[] { "source" }));
        DrawExhaustionAssertions.AssertAdvancedMatchesOracle(deck, 1, [new([])], []);
    }

    [Test]
    public void FullDrawBatchRunsItsConfiguredPostDrawRemoval() {
        Card source = new([], id: "source", drawCount: 3, drawOncePerTurn: false, postDrawDiscardCount: 2);
        List<Card> deck = [source, new([], id: "first"), new([], id: "second"), new([Role], id: "kept")];
        List<Combo> combos = [new([new(Role, 1, 1)])];

        List<Card> finalHand = DrawSequenceOracle.Resolve(deck, 1, combos)!;
        Assert.That(finalHand, Has.Count.EqualTo(1));
        Assert.That(finalHand.Single().Id, Is.EqualTo("kept"));
        DrawExhaustionAssertions.AssertAdvancedMatchesOracle(deck, 1, combos, []);
    }

    [Test]
    public void InsufficientFilteredBatchDoesNotFailForMissingRemovalMaterial() {
        Card source = new([], id: "source", drawCount: 3, drawOncePerTurn: false,
            postDrawDiscardCount: 1, postDrawSelector: CardSelector.For(Dark));
        List<Card> deck = [source, new([A], id: "first"), new([B], id: "second")];
        DrawSequenceOracle.Counts oracle = DrawSequenceOracle.Enumerate(deck, 1, [new([])], []);

        Assert.That(oracle.Failed, Is.EqualTo(BigInteger.Zero));
        Assert.That(DrawSequenceOracle.Resolve([source, deck[1], deck[2]], 1, [])!.Single().Id, Is.EqualTo("source"));
        DrawExhaustionAssertions.AssertAdvancedMatchesOracle(deck, 1, [new([])], []);
    }

    [Test]
    public void FullFilteredBatchRemovesTheEligibleDrawAndKeepsTheOtherCard() {
        Card source = new([], id: "source", drawCount: 2, drawOncePerTurn: false,
            postDrawDiscardCount: 1, postDrawSelector: CardSelector.For(Dark));
        Card eligible = new([Dark, B], id: "eligible");
        Card kept = new([A], id: "kept");
        List<Card> deck = [source, eligible, kept];

        Assert.That(DrawSequenceOracle.Resolve(deck, 1, [])!.Select(card => card.Id), Is.EqualTo(new[] { "kept" }));
        DrawExhaustionAssertions.AssertAdvancedMatchesOracle(deck, 1, Requirements(), Groups());
    }

    [Test]
    public void FullBatchRemovalCanRemoveANewlyDrawnCandidateBeforeItActivates() {
        Card source = new([], id: "source", drawCount: 2, drawOncePerTurn: false,
            postDrawDiscardCount: 1, postDrawSelector: CardSelector.For(Dark));
        Card pending = new([Dark], id: "pending", drawCount: 1, drawOncePerTurn: false,
            postDrawDiscardCount: 1, postDrawSelector: CardSelector.For(Dark));
        Card reward = new([B], id: "reward");
        List<Card> deck = [source, pending, reward];

        Assert.That(DrawSequenceOracle.Resolve(deck, 1, Requirements())!.Select(card => card.Id), Is.EqualTo(new[] { "reward" }));
        DrawExhaustionAssertions.AssertAdvancedMatchesOracle(deck, 1, Requirements(), Groups());
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
