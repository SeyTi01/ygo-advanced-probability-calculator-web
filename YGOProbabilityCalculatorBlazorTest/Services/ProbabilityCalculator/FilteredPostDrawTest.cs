using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Counting;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.DrawResolution;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public class FilteredPostDrawTest {
    internal static readonly CategoryBase Dark = new("Same label", CategorySource.Metadata, "attribute:dark");
    internal static readonly CategoryBase Quick = new("Same label", CategorySource.Metadata, "spell-type:quick-play");
    internal static readonly CategoryBase Radiant = new("Radiant", CategorySource.Metadata, "archetype:radiant%20typhoon");
    private static readonly CategoryBase Role = new("Role");
    private static readonly CardSelector Vision = CardSelector.AnyOf([Quick.Identity, Radiant.Identity]);

    private static Card Effect(string id, CardSelector selector, int copies = 1, bool opt = false) =>
        new([], copies, id: id, drawCount: 2, drawOncePerTurn: opt, postDrawDiscardCount: 1, postDrawSelector: selector);

    [TestCase("dark", false), TestCase("dark", true), TestCase("or", false), TestCase("or", true)]
    public void FilteredPostDrawPhysicalOracleCoversMembershipsChainsRowsAndStorage(string kind, bool opt) {
        CardSelector selector = kind == "dark" ? CardSelector.For(Dark) : Vision;
        List<Combo> combos = [
            new([new(Role, 1, 0, RequirementMaximumMode.HandSize)], groupId: "g"),
            new([new(Dark, 1, 2)], cards: [new("a", 1, 1)], groupId: "g"),
            new([], cards: [new("filtered", 1, 2)]),
            new([new(Quick, 0, 0)]),
            new([], alternativeGroups: [new([ComboAlternative.For(new ComboCategory(Radiant, 1, 2)),
                ComboAlternative.For(new ComboCard("b", 1, 1))])])
        ];
        List<ComboGroup> groups = [new("g", "Group"), new("empty", "Empty")];
        // Each seven-card deck exercises all 5,040 labeled orders. Alternatives
        // include atomic-only, either OR branch, both branches, and neither.
        foreach (CategoryBase[] membership in new CategoryBase[][] { [], [Dark], [Quick], [Radiant], [Quick, Radiant] }) {
            List<Card> deck = [Effect("filtered", selector, 2, opt),
                new([Dark, Quick, Role], id: "a"), new([Radiant, Role], id: "b"),
                new(membership, id: "middle", drawCount: 1, postDrawDiscardCount: 1),
                new([Dark, Quick, Radiant], id: "z"), new([], id: "f")];
            AssertParity(deck, 2, combos, groups);
        }
    }

    [TestCase(1), TestCase(2), TestCase(3)]
    public void FilteredPostDrawGracefulInteractionAndRemovalCounts(int handSize) {
        List<Card> deck = [Effect("filtered", Vision), new([Quick], id: "graceful", drawCount: 3,
            drawOncePerTurn: false, postDrawDiscardCount: 2), new([Radiant], 2, id: "eligible"),
            new([Role], 2, id: "target"), new([], id: "f")];
        AssertParity(deck, handSize, [new([new(Role, 1, 2)]), new([], cards: [new("eligible", 1, 2)])], []);
        deck[0] = deck[0].WithPostDrawDiscardCount(2);
        AssertParity(deck, handSize, [new([new(Role, 1, 2)])], []);
    }

    [Test]
    public void FilteredPostDrawVisionDuplicatesAreEligibleButConsumedSourceIsNot() {
        Card vision = Effect("vision", Vision, 2, true).WithCategories([Quick, Radiant]);
        List<Card> deck = [vision, new([Role], 2, id: "r"), new([], 2, id: "f")];
        AssertParity(deck, 2, [new([new(Role, 1, 2)]), new([], cards: [new("vision", 1, 2)])], []);
        Assert.That(DrawSequenceOracle.Resolve([vision, deck[1], deck[2]], 1, []), Is.Null);
        Assert.That(DrawSequenceOracle.Resolve([vision, vision, deck[1]], 1, [])!.Single().Id, Is.EqualTo("r"));
    }

    [Test]
    public void FilteredPostDrawInterleavedIdsCannotMergeAcrossDifferentEligibility() {
        List<Card> deck = [Effect("source", Vision), new([Quick], id: "a"), new([Radiant], id: "b"),
            new([Quick], id: "c"), new([], id: "d"), Effect("dark", CardSelector.For(Quick))];
        AssertParity(deck, 2, [new([])], []);
    }

    [Test]
    public void FilteredPostDrawTimingProtectsRelevantThenUsableEligibleCards() {
        Card source = Effect("source", CardSelector.For(Dark));
        Card critical = new([Dark, Role], id: "critical");
        Card expendable = new([Dark], id: "spare");
        Card pending = new([Dark], id: "pending", drawCount: 1);
        Card filler = new([], id: "filler");
        Card reward = new([Role], id: "reward");
        List<Combo> combos = [new([new(Role, 1, 3)])];
        Assert.That(DrawSequenceOracle.Resolve([source, critical, expendable, filler, pending, reward], 3, combos)!
            .Select(card => card.Id), Is.EquivalentTo(new[] { "critical", "filler", "reward" }));
        // With no spare DARK, the newly drawn unresolved effect is removed and
        // cannot draw the reward. Source consumption precedes eligibility checks.
        Assert.That(DrawSequenceOracle.Resolve([source, filler, pending, reward], 1, [])!
            .Select(card => card.Id), Is.EqualTo(new[] { "filler" }));
        Assert.That(DrawSequenceOracle.Resolve([source.WithCategories([Dark]), filler, filler], 1, []), Is.Null);
        Assert.That(DrawSequenceOracle.Resolve([source, expendable, filler], 1, [])!
            .Select(card => card.Id), Is.EqualTo(new[] { "filler" }));
        // Draw 2 cannot activate with one card left, so the source stays in hand and the other card stays in deck.
        Assert.That(DrawSequenceOracle.Resolve([source, expendable], 1, [])!.Select(card => card.Id),
            Is.EqualTo(new[] { "source" }));
    }

    [TestCase(false), TestCase(true)]
    public void FilteredPostDrawFailureRetainsDenominatorAndModesStayIsolated(bool qualifies) {
        List<Card> deck = [Effect("source", Vision), new(qualifies ? [Quick, Radiant] : [], id: "material"),
            new([], id: "target")];
        List<Combo> combos = [new([])];
        DrawSequenceOracle.Counts oracle = DrawSequenceOracle.Enumerate(deck, 1, combos, []);
        Assert.That(oracle.Total, Is.EqualTo(new System.Numerics.BigInteger(6)));
        Assert.That(oracle.Failed, Is.EqualTo(new System.Numerics.BigInteger(qualifies ? 0 : 2)));
        AssertParity(deck, 1, combos, []);
        foreach (bool global in new[] { false, true }) {
            foreach (bool advanced in new[] { false, true }) {
                List<Card> modeled = advanced ? deck : deck.Select(card => card.WithPostDrawDiscardCount(0)).ToList();
                CalculationSnapshot snapshot = CalculationSnapshot.Capture(modeled, combos, 1, [],
                    drawEffectsEnabled: global, options: new());
                ProbabilityCalculationResult result = CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json));
                Assert.That(result.TotalProbability, Is.EqualTo(global && advanced && !qualifies ? 2.0 / 3 : 1));
            }
        }
        WorkBudget filteredBasic = new(CalculationWorkPolicy.Interactive);
        WorkBudget pureBasic = new(CalculationWorkPolicy.Interactive);
        new DrawEffectCalculation(deck.Select(card => card.WithPostDrawDiscardCount(0)).ToList(), 1, filteredBasic).Calculate(combos, []);
        new DrawEffectCalculation(deck.Select(card => card.WithPostDrawDiscardCount(0)).ToList(), 1, pureBasic).Calculate(combos, []);
        Assert.That(filteredBasic.Spent, Is.EqualTo(pureBasic.Spent));
    }

    private static void AssertParity(List<Card> deck, int hand, List<Combo> combos, List<ComboGroup> groups) {
        DrawSequenceOracle.Counts oracle = DrawSequenceOracle.Enumerate(deck, hand, combos, groups);
        foreach (CalculationStorageTier tier in Enum.GetValues<CalculationStorageTier>()) {
            foreach (List<Card> order in new[] { deck, deck.AsEnumerable().Reverse().ToList() }) {
                WorkBudget budget = new(CalculationWorkPolicy.Interactive, CalculationStoragePolicy.ForTier(tier));
                ExactCalculationResult result = new PostDrawCalculation(order, hand, budget).Calculate(combos, groups);
                ExactProbability[] rows = [result.Total, .. result.Combos, .. result.Groups];
                for (int i = 0; i < rows.Length; i++) {
                    Assert.That(rows[i].Numerator * oracle.Total, Is.EqualTo(oracle.Wins[i] * rows[i].Denominator), $"{tier}, row {i}");
                }
            }
        }
    }
}
