using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public class PostDrawPolicyTest {
    private static readonly CategoryBase Role = new("Role");

    [Test]
    public void RelevantCardSurvivesFillerAndUsableEffect() {
        Card source = new([], id: "source", drawCount: 1, postDrawDiscardCount: 2);
        Card relevant = new([], id: "z-relevant");
        Card pending = new([], id: "a-pending", drawCount: 1);
        Card filler = new([], id: "filler");
        List<Combo> combos = [new([], cards: [new(relevant.Id, 1, 1)])];
        List<Card>? hand = DrawSequenceOracle.Resolve([source, relevant, filler, pending, filler], 3, combos);
        Assert.That(hand!.Select(card => card.Id), Is.EqualTo(new[] { relevant.Id }));
    }

    [Test]
    public void UsableEffectSurvivesFillerAndResolvesAfterDiscard() {
        Card source = new([], id: "source", drawCount: 1, postDrawDiscardCount: 1);
        Card pending = new([], id: "z-pending", drawCount: 1);
        Card filler = new([], id: "a-filler");
        Card reward = new([], id: "reward");
        Assert.That(DrawSequenceOracle.Resolve([source, filler, pending, reward], 2, [])!
            .Select(card => card.Id), Is.EqualTo(new[] { reward.Id }));
    }

    [Test]
    public void RedundantOptCopyIsDiscardedBeforeUsableCandidate() {
        Card source = new([], id: "source", drawCount: 1, postDrawDiscardCount: 1);
        Card pending = new([], id: "a-pending", drawCount: 1);
        Card reward = new([], id: "reward");
        Assert.That(DrawSequenceOracle.Resolve([source, pending, pending, reward], 2, [])!
            .Select(card => card.Id), Is.EqualTo(new[] { reward.Id }));
    }

    [Test]
    public void CanonicalIdTieKeepsSmallerIdRegardlessOfEncounterOrder() {
        Card source = new([], drawCount: 1, postDrawDiscardCount: 1);
        Card first = new([], id: "a");
        Card last = new([], id: "z");
        Assert.That(DrawSequenceOracle.Resolve([source, first, last], 2, [])!.Single().Id, Is.EqualTo("a"));
        Assert.That(DrawSequenceOracle.Resolve([source, last, first], 2, [])!.Single().Id, Is.EqualTo("a"));
    }

    [TestCase("direct"), TestCase("category"), TestCase("or"), TestCase("maximum"), TestCase("exclusion")]
    public void OnlyPositiveRequirementsProtectCards(string kind) {
        Card source = new([], drawCount: 1, postDrawDiscardCount: 1);
        Card role = new([Role], id: "z");
        Card filler = new([], id: "a");
        Combo combo = kind switch {
            "direct" => new([], cards: [new("z", 1, 1)]),
            "category" => new([new(Role, 1, 1)]),
            "or" => new([], alternativeGroups: [new([ComboAlternative.For(new ComboCategory(Role, 1, 1))])]),
            "maximum" => new([new(Role, 0, 1)]),
            _ => new([], cards: [new("z", 0, 0)])
        };
        string expected = kind is "maximum" or "exclusion" ? "a" : "z";
        Assert.That(DrawSequenceOracle.Resolve([source, role, filler], 2, [combo])!.Single().Id, Is.EqualTo(expected));
    }

    [TestCase(1, 1, 4), TestCase(3, 2, 5)]
    public void BatchedEffectConsumesSourceThenDiscards(int draw, int discard, int finalSize) {
        Card source = new([], drawCount: draw, drawOncePerTurn: false, postDrawDiscardCount: discard);
        Card[] order = [source, .. Enumerable.Range(0, 7).Select(index => new Card([], id: $"c{index}"))];
        Assert.That(DrawSequenceOracle.Resolve(order, 5, [])!.Count, Is.EqualTo(finalSize));
    }
}
