using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public class PostDrawFrontierTest {
    private static readonly CategoryBase Material = new("Material");
    private static readonly CategoryBase Role = new("Role");

    [TestCase(false, 0), TestCase(false, 2), TestCase(false, 3), TestCase(false, 6)]
    [TestCase(true, 0), TestCase(true, 2), TestCase(true, 3), TestCase(true, 6)]
    public void SettledFrontiersRetainExactMassAcrossSiblingDraws(bool opt, int handSize) {
        // Two copies create duplicate non-OPT queue entries or a single OPT
        // entry. Costs reserve the activating copy; filtered removals can erase
        // queued copies, fail a branch, or leave a deferred source for retry.
        Card source = new([Material], 2, id: "source", drawCount: 2, drawOncePerTurn: opt,
            preDrawRemovalCount: 1, preDrawSelector: CardSelector.For(Material),
            postDrawDiscardCount: 1, postDrawSelector: CardSelector.For(Material));
        Card other = new([], id: "other", drawCount: 1, drawOncePerTurn: false, postDrawDiscardCount: 2);
        List<Card> deck = [source, other, new([Material, Role], id: "a"),
            new([Material], id: "b"), new([Role], id: "c"), new([], id: "z")];
        List<Combo> combos = [
            new([], "Surviving branch", groupId: "g"),
            new([new(Material, 0, 0)], "No material"),
            new([ComboCategory.SameCard([Material, Role], 1, 1), new(Role, 1, 2)], "Distinct copies", groupId: "g"),
            new([], "Source OR role", cards: [new("b", 0, 0)], alternativeGroups: [new([
                ComboAlternative.For(new ComboCard("source", 1, 2)),
                ComboAlternative.For(new ComboCategory(Role, 1, 1))
            ])])
        ];

        PreDrawCostsTest.AssertParity(deck, handSize, combos, [new("g", "Group"), new("empty", "Empty")]);
    }

    [Test]
    public void SameHandCountsWithDifferentFifoOrdersRemainDistinct() {
        Card first = new([], id: "first", drawCount: 1, postDrawDiscardCount: 1);
        Card second = new([], id: "second", drawCount: 2, postDrawDiscardCount: 1);
        Card material = new([], id: "material");
        Card reward = new([Role], id: "reward");
        List<Combo> combos = [new([new(Role, 1, 1)], groupId: "g"),
            new([], cards: [new("second", 0, 0)])];
        List<Card> deck = [first, second, material, reward];
        string[] forward = DrawSequenceOracle.Resolve(deck, 2, combos)!.Select(card => card.Id).Order().ToArray();
        string[] reverse = DrawSequenceOracle.Resolve([second, first, material, reward], 2, combos)!
            .Select(card => card.Id).Order().ToArray();

        Assert.That(forward, Is.Not.EqualTo(reverse), "The fixture must distinguish FIFO order with identical opening counts.");
        PreDrawCostsTest.AssertParity(deck, 2, combos, [new("g", "Group")]);
    }
}
