using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public class PostDrawPolicyTest {
    private static readonly CategoryBase Role = new("Role");
    private static readonly CategoryBase Dark = new("Dark");

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
    public void DeferredDrawSourceSurvivesFillerWhenAnotherQueuedDrawCanSupplyCost() {
        Card b = new([], id: "b-post-draw", drawCount: 1, postDrawDiscardCount: 1);
        Card a = new([], id: "a-deferred", drawCount: 1, preDrawRemovalCount: 1,
            preDrawSelector: CardSelector.For(Dark));
        Card c = new([], id: "c-draw", drawCount: 1);
        Card filler = new([], id: "filler");
        Card material = new([Dark], id: "dark-material");
        Card reward = new([], id: "reward");
        Combo combo = new([], cards: [new(reward.Id, 1, 1)]);

        List<Card>? hand = DrawSequenceOracle.Resolve([b, a, c, filler, material, reward], 3, [combo]);

        string[] resolvedIds = hand!.Select(card => card.Id).ToArray();
        Assert.That(resolvedIds, Is.EqualTo(new[] { reward.Id }), $"Resolved hand: {string.Join(", ", resolvedIds)}");
        PreDrawCostsTest.AssertParity([b, a, c, filler, material, reward], 3, [combo], []);
    }

    [Test]
    public void ReservedPreDrawSourceKeepsDeferredCardAheadOfFiller() {
        Card b = new([], id: "b-pre-draw", drawCount: 1, preDrawRemovalCount: 1);
        Card a = new([], id: "a-deferred", drawCount: 1, preDrawRemovalCount: 1,
            preDrawSelector: CardSelector.For(Dark));
        Card filler = new([], id: "filler");
        Card material = new([Dark], id: "dark-material");
        Card reward = new([], id: "reward");
        Combo combo = new([], cards: [new(reward.Id, 1, 1)]);

        List<Card>? hand = DrawSequenceOracle.Resolve([b, a, filler, material, reward], 3, [combo]);

        Assert.That(hand!.Select(card => card.Id), Is.EqualTo(new[] { reward.Id }));
        PreDrawCostsTest.AssertParity([b, a, filler, material, reward], 3, [combo], []);
    }

    [Test]
    public void PositiveRequirementThenUsableSourceThenDeferredSourceSurviveFillerRemoval() {
        Card b = new([], id: "b-post-draw", drawCount: 1, postDrawDiscardCount: 2);
        Card positive = new([], id: "positive");
        Card deferred = new([], id: "a-deferred", drawCount: 1, preDrawRemovalCount: 1,
            preDrawSelector: CardSelector.For(Dark));
        Card usable = new([], id: "c-usable", drawCount: 1);
        Card filler = new([], id: "filler");
        Card material = new([Dark], id: "dark-material");
        Card reward = new([], id: "reward");
        Combo combo = new([], cards: [new(positive.Id, 1, 1)]);

        List<Card>? hand = DrawSequenceOracle.Resolve([b, positive, deferred, usable, filler, material, reward], 4, [combo]);

        Assert.That(hand!.Select(card => card.Id), Is.EquivalentTo(new[] { positive.Id, material.Id }));
        PreDrawCostsTest.AssertParity([b, positive, deferred, usable, filler, material, reward], 4, [combo], []);
    }

    [Test]
    public void UsableSourceOutranksDeferredSourceWithinCompositeRemovalEligibility() {
        CategoryBase deferredRole = new("Deferred role");
        CategoryBase usableRole = new("Usable role");
        CategoryBase monster = new("Monster");
        Card b = new([], id: "b-post-draw", drawCount: 1, postDrawDiscardCount: 1,
            postDrawSelector: CardSelector.AnyOf([deferredRole.Identity, usableRole.Identity]));
        Card deferred = new([deferredRole], id: "a-deferred", drawCount: 1, preDrawRemovalCount: 1,
            preDrawSelector: CardSelector.AllOf([Dark.Identity, monster.Identity]));
        Card usable = new([usableRole], id: "c-usable", drawCount: 1);
        Card filler = new([], id: "filler");
        Card material = new([Dark, monster], id: "material");
        Card reward = new([], id: "reward");

        List<Card>? hand = DrawSequenceOracle.Resolve([b, deferred, usable, filler, material, reward], 3, []);

        Assert.That(hand!.Select(card => card.Id), Is.EquivalentTo(new[] { filler.Id, material.Id }));
        PreDrawCostsTest.AssertParity([b, deferred, usable, filler, material, reward], 3, [], []);
    }

    [Test]
    public void DeferredSourceIsNotProtectedWhenNoOtherDrawCanResolve() {
        Card b = new([], id: "b-post-draw", drawCount: 1, postDrawDiscardCount: 1);
        Card deferred = new([], id: "a-deferred", drawCount: 1, preDrawRemovalCount: 1,
            preDrawSelector: CardSelector.For(Dark));
        Card filler = new([], id: "filler");
        Card material = new([Dark], id: "material");
        Card reward = new([], id: "reward");

        List<Card>? hand = DrawSequenceOracle.Resolve([b, deferred, filler, material, reward], 2, []);

        Assert.That(hand!.Select(card => card.Id), Is.EqualTo(new[] { filler.Id }));
        PreDrawCostsTest.AssertParity([b, deferred, filler, material, reward], 2, [], []);
    }

    [Test]
    public void DeferredSourceIsNotProtectedWhenItsBatchCannotFitAfterAnotherDraw() {
        Card b = new([], id: "b-post-draw", drawCount: 1, postDrawDiscardCount: 1);
        Card deferred = new([], id: "a-deferred", drawCount: 2, preDrawRemovalCount: 1,
            preDrawSelector: CardSelector.For(Dark));
        Card usable = new([], id: "c-usable", drawCount: 1);
        Card filler = new([], id: "filler");
        Card material = new([Dark], id: "material");
        Card reward = new([], id: "reward");

        List<Card>? hand = DrawSequenceOracle.Resolve([b, deferred, usable, filler, material, reward], 3, []);

        Assert.That(hand!.Select(card => card.Id), Is.EquivalentTo(new[] { filler.Id, material.Id }));
        PreDrawCostsTest.AssertParity([b, deferred, usable, filler, material, reward], 3, [], []);
    }

    [Test]
    public void SpentOptCopyWithoutLiveQueueIsNotCostDeferred() {
        Card opt = new([], id: "a-opt", drawCount: 1, drawOncePerTurn: true,
            preDrawRemovalCount: 1, preDrawSelector: CardSelector.For(Dark));
        Card b = new([], id: "b-post-draw", drawCount: 1, postDrawDiscardCount: 1);
        Card material = new([Dark], id: "dark-material");
        Card filler = new([], id: "filler");
        Card reward = new([], id: "reward");

        List<Card>? hand = DrawSequenceOracle.Resolve([opt, opt, b, material, filler, reward], 4, []);

        Assert.That(hand!.Select(card => card.Id), Is.EquivalentTo(new[] { filler.Id, reward.Id }));
        PreDrawCostsTest.AssertParity([opt, opt, b, material, filler, reward], 4, [], []);
    }

    [Test]
    public void RedundantUnspentOptCopiesAreNotDeferredUntilOnlyOneCopyRemains() {
        Card opt = new([], id: "a-opt", drawCount: 1, drawOncePerTurn: true,
            preDrawRemovalCount: 1, preDrawSelector: CardSelector.For(Dark));
        Card b = new([], id: "b-post-draw", drawCount: 1, postDrawDiscardCount: 1);
        Card c = new([], id: "c-draw", drawCount: 1);
        Card filler = new([], id: "filler");
        Card material = new([Dark], id: "dark-material");
        Card reward = new([], id: "reward");

        List<Card>? hand = DrawSequenceOracle.Resolve([b, opt, opt, c, filler, material, reward], 4, []);

        Assert.That(hand!.Select(card => card.Id), Is.EquivalentTo(new[] { filler.Id, reward.Id }));
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
