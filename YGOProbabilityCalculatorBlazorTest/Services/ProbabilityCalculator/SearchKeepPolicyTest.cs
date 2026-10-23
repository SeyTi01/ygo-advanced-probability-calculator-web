using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.DrawResolution;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

public sealed partial class SearchEffectsTest {
    [TestCase(false, "piece")]
    [TestCase(true, "piece")]
    [TestCase(false, "irrelevant")]
    [TestCase(true, "irrelevant")]
    [TestCase(false, "absent")]
    [TestCase(true, "absent")]
    [TestCase(false, "held")]
    [TestCase(true, "held")]
    [TestCase(false, "capacity")]
    [TestCase(true, "capacity")]
    public void LabeledPolicyProtectsOnlyPlausibleSearches(bool preCost, string scenario) {
        Card source = new Card([B], id: "search").WithSearch(new(CardSelector.For(A), false));
        Card draw = new([B], id: "other-draw", drawCount: 1);
        Card operation = new([], id: "operation", drawCount: 1,
            preDrawRemovalCount: preCost ? 1 : 0, preDrawSelector: preCost ? CardSelector.For(B) : null,
            postDrawDiscardCount: preCost ? 0 : 1, postDrawSelector: preCost ? null : CardSelector.For(B));
        Card target = new(scenario == "absent" ? [] : [A], id: "target");
        List<Card> order = [operation, source, draw];
        if (scenario == "held") {
            order.Add(target);
        }
        int size = order.Count;
        order.Add(new([], id: "next"));
        if (scenario != "capacity") {
            if (scenario != "held") {
                order.Add(target);
            }
            order.Add(new([], id: "last"));
        }
        List<Combo> combos = scenario == "irrelevant"
            ? [new([new(A, 0, 0)], groupId: "g1")]
            : [new([], cards: [new("target", 1, 5)], groupId: "g1")];
        if (scenario == "capacity") {
            // This physical card remains protected even with no deck left.
            combos.Add(new([], cards: [new(draw.Id, 1, 5)], groupId: "g2"));
        }
        List<Card> hand = DrawSequenceOracle.Resolve(order, size, combos)!;
        bool protectedSearch = scenario is "piece" or "irrelevant";
        Assert.That(hand.Any(card => card.Id == source.Id), Is.EqualTo(protectedSearch), scenario);
        if (protectedSearch) {
            Assert.That(hand.Any(card => card.Id == draw.Id), Is.False, "Equal tiers keep the canonical ordinary/effect tie.");
        }
        if (scenario == "held") {
            Assert.That(hand.Any(card => card.Id == target.Id), Is.True, "An already held positive piece stays protected.");
        }
        Compare(order, combos, size);
        Compare(order, combos, size, new(DrawActivationMode.CustomPriority, [draw.Id, operation.Id]));
        order.Reverse();
        Compare(order, combos, size);
    }

    [TestCase(false, false, 1)]
    [TestCase(true, false, 1)]
    [TestCase(false, true, 1)]
    [TestCase(true, true, 2)]
    public void CompetingSearchersCopiesAndHiddenCapacityMatchAllExactRows(bool preCost, bool hidden, int copies) {
        Card first = new Card([B], copies, id: "a-source").WithSearch(new(CardSelector.For(A), false));
        Card second = new Card([B], id: "z-source").WithSearch(new(CardSelector.For(A), false));
        List<Card> deck = [first, second, new([A], id: "target"), new([A, B], id: "both"),
            new([], id: "operation", drawCount: 1, preDrawRemovalCount: preCost ? 1 : 0,
                postDrawDiscardCount: preCost ? 0 : 1, preDrawDeckRemovalCount: hidden ? 1 : 0),
            new([], id: "blank")];
        List<Combo> combos = [new([new(A, 2, 2)], groupId: "g1"),
            new([new(A, 1, 2), new(B, 1, 2)], groupId: "g2"),
            new([new(B, 0, 0)], cards: [new("target", 1, 2)], groupId: "g1"),
            new([], alternativeGroups: [new([ComboAlternative.For(ComboCategory.SameCard([A, B], 1, 2)),
                ComboAlternative.For(new ComboCard("target", 1, 2))])], groupId: "g2")];
        foreach (int size in new[] { 2, deck.Sum(card => card.Copies) - 1 }) {
            Compare(deck, combos, size);
        }
        deck.Reverse();
        Compare(deck, combos, 2);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TwoSearchersCompetingForOneCopyKeepCanonicalTies(bool preCost) {
        Card first = new Card([B], id: "a-source").WithSearch(new(CardSelector.For(A), false));
        Card second = new Card([B], id: "z-source").WithSearch(new(CardSelector.For(A), false));
        List<Card> order = [new([], id: "operation", drawCount: 1,
                preDrawRemovalCount: preCost ? 1 : 0, preDrawSelector: preCost ? CardSelector.For(B) : null,
                postDrawDiscardCount: preCost ? 0 : 1, postDrawSelector: preCost ? null : CardSelector.For(B)),
            first, second, new([], id: "next"), new([A], id: "only-target")];
        List<Combo> combos = [new([new(A, 1, 3)], groupId: "g1"), new([new(A, 2, 3)], groupId: "g2"),
            new([new(B, 0, 0)], cards: [new("only-target", 1, 3)], groupId: "g1")];
        List<Card> hand = DrawSequenceOracle.Resolve(order, 3, combos)!;
        Assert.That(hand.Any(card => card.Id == first.Id), Is.True);
        Assert.That(hand.Any(card => card.Id == second.Id), Is.False);
        Compare(order, combos, 3);
        order.Reverse();
        Compare(order, combos, 3);
    }

    [TestCase(1)]
    [TestCase(2)]
    public void ForcedRemovalNeverResurrectsAPooledSource(int copies) {
        List<Card> deck = [new Card([B], copies, id: "source").WithSearch(new(CardSelector.For(A), false)),
            new([A], id: "target"), new([B], id: "blank"), new([], id: "draw", drawCount: 1,
                postDrawDiscardCount: 2, postDrawSelector: CardSelector.For(B)), new([], id: "other")];
        List<Combo> combos = [new([new(A, 1, 2)], groupId: "g1"), new([new(A, 0, 0)], groupId: "g2")];
        Compare(deck, combos, 2);
        Compare(deck, combos, 3);
    }

    [Test]
    public void SeededMixedSearchPoliciesMatchLabeledReference() {
        Random random = new(20315);
        for (int trial = 0; trial < 16; trial++) {
            Card first = new Card([B], id: "first").WithSearch(new(CardSelector.For(A), false));
            Card second = new Card([A], id: "second").WithSearch(new(CardSelector.AnyOf([A.Identity, B.Identity]), false));
            List<Card> deck = [first, second];
            for (int target = 0; target < 3; target++) {
                int bits = random.Next(4);
                deck.Add(new(new[] { A, B }.Where((_, bit) => (bits & (1 << bit)) != 0), id: $"target-{target}"));
            }
            bool preCost = trial % 2 == 0;
            CardSelector selector = trial % 3 == 0 ? CardSelector.Any : CardSelector.For(B);
            deck.Add(new([], id: "draw", drawCount: 1, preDrawRemovalCount: preCost ? 1 : 0,
                preDrawSelector: preCost ? selector : null, postDrawDiscardCount: preCost ? 0 : 1,
                postDrawSelector: preCost ? null : selector, postDrawDeckMillCount: trial % 4 == 0 ? 1 : 0));
            List<Combo> combos = [new([new(A, 1, 1), new(B, 1, 3)], groupId: "g1"),
                new([new(A, 0, 0)], cards: [new("target-0", 1, 2)], groupId: "g2"),
                new([ComboCategory.SameCard([A, B], 1, 2)], groupId: "g1")];
            Compare(deck, combos, 2);
            deck.Reverse();
            Compare(deck, combos, 3);
        }
    }

    [Test]
    public void UnobservedOrdinaryLabelsDeferTogetherWithUnremovableSearchSources() {
        List<Card> deck = [new Card([Spell], id: "source").WithSearch(new(CardSelector.For(A), false)),
            new([A], id: "target"), new([B], id: "material"),
            new([], id: "draw", drawCount: 1, preDrawRemovalCount: 1, preDrawSelector: CardSelector.For(B)),
            new([A], id: "ordinary-a"), new([], id: "ordinary-blank")];
        List<Combo> combos = [new([new(A, 1, 2)], groupId: "g1"), new([new(Spell, 1, 2)], groupId: "g2"),
            new([], cards: [new("target", 1, 2)], groupId: "g1")];
        WorkBudget budget = new(CalculationWorkPolicy.Interactive);
        PostDrawCalculation calculation = new(deck, 2, budget);
        calculation.CalculateSearch(combos, Groups, false, new(deck, budget));
        Assert.That(calculation.DeferredRows, Is.EqualTo(4));
        Compare(deck, combos, 2);
    }
}
