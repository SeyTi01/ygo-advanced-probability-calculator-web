using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Counting;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.DrawResolution;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public class SameCardSelectorTest {
    internal static readonly CategoryBase A = new("Orcust", CategorySource.Metadata, "archetype:orcust");
    internal static readonly CategoryBase B = new("Monster", CategorySource.Metadata, "card-type:monster");
    internal static readonly CategoryBase C = new("Extender");
    internal static readonly CategoryBase D = new("World Legacy", CategorySource.Metadata, "archetype:world%20legacy");

    internal static CardSelector Composite => CardSelector.AnyOfSelectors([
        CardSelector.AllOf([A.Identity, B.Identity]), CardSelector.AllOf([D.Identity, B.Identity])
    ]);

    [Test]
    public void SameCardNormalizationPreservesIdentityAndOperator() {
        CardSelector ab = CardSelector.AllOf([A.Identity, B.Identity, A.Identity]);
        Assert.That(ab, Is.EqualTo(CardSelector.AllOf([B.Identity, A.Identity])));
        Assert.That(CardSelector.AllOf([A.Identity, A.Identity]), Is.EqualTo(CardSelector.For(A)));
        Assert.That(CardSelector.AnyOfSelectors([ab, ab]), Is.EqualTo(ab));
        Assert.That(CardSelector.AnyOfSelectors([Composite, ab]), Is.EqualTo(Composite));
        CardSelector reverseAuthored = CardSelector.AnyOfSelectors(Composite.Children.Reverse());
        Assert.That(reverseAuthored.Signature, Is.EqualTo(Composite.Signature));
        Assert.That(reverseAuthored.Terms.Select(term => term.Signature), Is.EqualTo(Composite.Terms.Select(term => term.Signature)));
        Assert.That(JsonSerializer.Deserialize<CardSelector>(JsonSerializer.Serialize(reverseAuthored)), Is.EqualTo(Composite));
        CardSelector renamed = CardSelector.AnyOfSelectors([ab, CardSelector.AllOf([C.Identity, B.Identity])])
            .RenameCategory(C.Identity, "user:Renamed");
        Assert.That(renamed.Children.All(child => child.Kind == CardSelectorKind.AllOf), Is.True);
        Assert.That(renamed.CategoryIdentities, Does.Contain(A.Identity).And.Contain("user:Renamed").And.Not.Contain(C.Identity));
        CategoryBase sameName = new(A.Name);
        Assert.That(ab.Matches([sameName, B]), Is.False);
        Assert.That(CardSelector.AllOf([sameName.Identity, B.Identity]).Matches([sameName, B]), Is.True);
        foreach (CardSelector selector in new[] { ab, Composite, CardSelector.Any, CardSelector.For(A), CardSelector.AnyOf([A.Identity, B.Identity]) }) {
            Assert.That(JsonSerializer.Deserialize<CardSelector>(JsonSerializer.Serialize(selector)), Is.EqualTo(selector));
        }
    }

    [TestCase("{\"Kind\":\"AllOf\",\"Categories\":[]}")]
    [TestCase("{\"Kind\":\"AnyOf\",\"Children\":[]}")]
    [TestCase("{\"Kind\":\"AnyOf\",\"Children\":[{\"Kind\":\"Any\",\"Categories\":[]}]}")]
    [TestCase("{\"Kind\":\"AnyOf\",\"Children\":[{\"Kind\":\"AllOf\",\"Categories\":[\"user:A\"],\"Other\":0}]}")]
    [TestCase("{\"Kind\":\"AnyOf\",\"Children\":[],\"Categories\":[]}")]
    public void SameCardMalformedSelectorsFailClosed(string json) {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CardSelector>(json));
    }

    [Test]
    public void SameCardRawPayloadAndCompiledMasksKeepSeparateStorageAndWorkLimits() {
        string atoms = JsonSerializer.Serialize(Enumerable.Repeat("user:A", 1025));
        string oversized = $$"""{"Kind":"AnyOf","Children":[{"Kind":"AllOf","Categories":{{atoms}}},{"Kind":"AllOf","Categories":{{atoms}}}]}""";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CardSelector>(oversized));
        CategoryBase longIdentity = new(new string('x', 270000));
        Combo combo = new([ComboCategory.SameCard([longIdentity, B], 1, 1)]);
        List<Card> deck = [new([longIdentity, B])];
        ProbabilityCalculationLimitException? storage = Assert.Throws<ProbabilityCalculationLimitException>(() =>
            new ProbabilityCalculation(deck, 1, new(CalculationWorkPolicy.Interactive)).CalculateUnionExact([combo]));
        Assert.That(storage!.Reason, Is.EqualTo(ProbabilityCalculationLimitReason.Storage));
        foreach (CalculationStorageTier tier in Enum.GetValues<CalculationStorageTier>()) {
            ProbabilityCalculationLimitException? work = Assert.Throws<ProbabilityCalculationLimitException>(() =>
                new ProbabilityCalculation(deck, 1, new(new CalculationWorkPolicy(1), CalculationStoragePolicy.ForTier(tier))).CalculateUnionExact([combo]));
            Assert.That(work!.Reason, Is.EqualTo(ProbabilityCalculationLimitReason.Work));
        }
        AssertExact(new ProbabilityCalculation(deck, 1, new(CalculationWorkPolicy.Interactive, CalculationStoragePolicy.High))
            .CalculateUnionExact([combo]), (1, 1));
    }

    [TestCase(1), TestCase(2), TestCase(3), TestCase(4)]
    public void SameCardHallBoundsAndOrRoutesMatchEveryPhysicalHand(int handSize) {
        List<Card> deck = [new([A, B, C], 2, id: "abc"), new([A], id: "a"), new([B], id: "b"),
            new([B, D], id: "bd"), new([C], id: "c"), new([], id: "f")];
        ComboCategory ab = ComboCategory.SameCard([A, B], 1, 0, RequirementMaximumMode.HandSize);
        List<Combo> combos = [
            new([ab]), new([ComboCategory.SameCard([A, B], 2, 0, RequirementMaximumMode.HandSize)]),
            new([ab, new(C, 1, 0, RequirementMaximumMode.HandSize)]),
            new([ab, new(C, 0, 1)]), new([ComboCategory.SameCard([A, B], 0, 0)]),
            new([ComboCategory.SameCard([B, C], 1, 3)]),
            new([ab, ComboCategory.SameCard([B, A], 2, 2)]),
            new([ComboCategory.SameCard([A, B], 0, 0), ab]),
            new([], alternativeGroups: [new([ComboAlternative.For(ab), ComboAlternative.For(new ComboCategory(C, 1, 4))])]),
            new([], alternativeGroups: [new([ComboAlternative.For(ab), ComboAlternative.For(ComboCategory.SameCard([B, D], 1, 4)),
                ComboAlternative.For(ComboCategory.SameCard([B, A], 1, 0, RequirementMaximumMode.HandSize)), ComboAlternative.For(new ComboCard("c", 1, 1))])])
        ];
        foreach (List<Card> order in new[] { deck, deck.AsEnumerable().Reverse().ToList() }) {
            foreach (CalculationStorageTier tier in Enum.GetValues<CalculationStorageTier>()) {
                WorkBudget budget = new(CalculationWorkPolicy.Interactive, CalculationStoragePolicy.ForTier(tier));
                ExactCalculationResult result = new ProbabilityCalculation(order, handSize, budget).CalculateExactResults(combos, []);
                AssertExact(result.Total, SmallDeckOracle.EnumerateCounts(deck, combos, handSize));
                for (int index = 0; index < combos.Count; index++) {
                    AssertExact(result.Combos[index], SmallDeckOracle.EnumerateCounts(deck, [combos[index]], handSize));
                }
            }
        }
    }

    [Test]
    public void SameCardRequiresWholeIntersectionAndSeparateRolesNeedDistinctCopies() {
        ComboCategory ab = ComboCategory.SameCard([A, B], 1, 2);
        Card all = new([A, B, C]);
        foreach (List<Card> hand in new List<Card>[] { [new([A])], [new([B])], [new([A]), new([B])] }) {
            Assert.That(SmallDeckOracle.MatchesHand(hand, new([ab])), Is.False);
            AssertExact(new ProbabilityCalculation(hand, hand.Count, new(CalculationWorkPolicy.Interactive)).CalculateUnionExact([new([ab])]), (0, 1));
        }
        Assert.That(SmallDeckOracle.MatchesHand([all], new([ab])), Is.True);
        Assert.That(SmallDeckOracle.MatchesHand([all], new([ab, new(C, 1, 2)])), Is.False);
        Assert.That(SmallDeckOracle.MatchesHand([all, all], new([ab, new(C, 1, 2)])), Is.True);
    }

    [TestCase(false), TestCase(true)]
    public void SameCardAdvancedSelectorsMatchEveryLabeledDrawOrder(bool orOfAnd) {
        CardSelector selector = orOfAnd ? Composite : CardSelector.AllOf([A.Identity, B.Identity]);
        List<Card> deck = [new([], id: "source", drawCount: 2, postDrawDiscardCount: 1, postDrawSelector: selector),
            new([A], id: "partial"), new([A, B, C], 2, id: "abc"), new([B, D], id: "pending", drawCount: 1), new([], id: "f")];
        List<Combo> combos = [new([ComboCategory.SameCard([A, B], 1, 3)]), new([new(C, 1, 3)]),
            new([ComboCategory.SameCard([B, D], 0, 0)]), new([], cards: [new("pending", 1, 2)])];
        DrawSequenceOracle.Counts oracle = DrawSequenceOracle.Enumerate(deck, 2, combos, []);
        foreach (CalculationStorageTier tier in Enum.GetValues<CalculationStorageTier>()) {
            foreach (List<Card> order in new[] { deck, deck.AsEnumerable().Reverse().ToList() }) {
                ExactCalculationResult result = new PostDrawCalculation(order, 2,
                    new(CalculationWorkPolicy.Interactive, CalculationStoragePolicy.ForTier(tier))).Calculate(combos, []);
                ExactProbability[] rows = [result.Total, .. result.Combos];
                for (int index = 0; index < rows.Length; index++) {
                    Assert.That(rows[index].Numerator * oracle.Total, Is.EqualTo(oracle.Wins[index] * rows[index].Denominator));
                }
            }
        }
        foreach (bool global in new[] { false, true }) {
            foreach (bool advanced in new[] { false, true }) {
                CalculationSnapshot snapshot = CalculationSnapshot.Capture(advanced ? deck : deck.Select(card => card.WithPostDrawDiscardCount(0)), combos, 2, [], drawEffectsEnabled: global, options: new());
                double actual = CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json)).TotalProbability;
                List<Card> effective = global ? deck.Select(card => card.WithPostDrawDiscardCount(0)).ToList()
                    : deck.Select(card => card.WithDrawCount(null)).ToList();
                double expected = global && advanced ? (double)oracle.Wins[0] / (double)oracle.Total
                    : new ProbabilityCalculatorService().CalculateProbabilityResults(effective, combos, 2).TotalProbability;
                Assert.That(actual, Is.EqualTo(expected).Within(1e-12));
            }
        }
    }

    [Test]
    public void SameCardRemovalRejectsPartialMatchesAndRemovesPendingBeforeActivation() {
        Card effect = new([], id: "source", drawCount: 2, postDrawDiscardCount: 1,
            postDrawSelector: CardSelector.AllOf([A.Identity, B.Identity]));
        Card partial = new([A], id: "partial");
        Card pending = new([A, B], id: "pending", drawCount: 1);
        Card reward = new([C], id: "reward");
        Assert.That(DrawSequenceOracle.Resolve([effect, partial, partial], 1, []), Is.Null);
        Assert.That(DrawSequenceOracle.Resolve([effect, partial, pending, reward], 1, [])!.Select(card => card.Id), Is.EqualTo(new[] { "partial" }));
        Card protectedCard = new([A, B, C], id: "protected");
        List<Combo> combos = [new([ComboCategory.SameCard([A, C], 1, 3)])];
        Assert.That(DrawSequenceOracle.Resolve([effect, protectedCard, pending, reward], 1, combos)!.Select(card => card.Id), Is.EqualTo(new[] { "protected" }));
    }

    private static void AssertExact(ExactProbability actual, (int Successes, int Total) expected) {
        Assert.That(actual.Numerator * expected.Total, Is.EqualTo(expected.Successes * actual.Denominator));
    }
}
