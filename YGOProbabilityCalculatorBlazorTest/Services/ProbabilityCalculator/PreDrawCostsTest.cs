using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.DeckImport;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Counting;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.DrawResolution;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public class PreDrawCostsTest {
    internal static readonly CategoryBase Monster = new CategoryBase("Monster", CategorySource.Metadata, "kind:monster");
    internal static readonly CategoryBase Level = new CategoryBase("Level 8", CategorySource.Metadata, "level:8");
    internal static readonly CategoryBase Orcust = new CategoryBase("Archetype: Orcust", CategorySource.Metadata, "archetype:orcust");
    internal static readonly CategoryBase Legacy = new CategoryBase("Archetype: World Legacy", CategorySource.Metadata, "archetype:world%20legacy");
    private static readonly CategoryBase Role = new("Role");
    internal static Card Trade(string id = "trade", int copies = 1, bool opt = false) => new([], copies,
        name: "Same display name", id: id, externalCardId: 38120068, drawCount: 2, drawOncePerTurn: opt,
        preDrawRemovalCount: 1, preDrawSelector: CardSelector.For(Level));

    [TestCase(false), TestCase(true)]
    public void PreDrawPhysicalOracleCoversEligibilityIdentityAndMixedChains(bool opt) {
        foreach (CategoryBase[] membership in new CategoryBase[][] { [], [Level], [Monster], [Level, Monster],
            [Orcust], [Legacy], [Orcust, Monster], [Legacy, Monster], [Orcust, Legacy, Monster] }) {
            Card returnCard = new([Level, Monster], name: "Same display name", id: "return", externalCardId: 26845680,
                drawCount: 2, drawOncePerTurn: opt, preDrawRemovalCount: 1,
                preDrawSelector: CuratedDrawEffectPresets.All.Single(preset => preset.CardId == 26845680).PreDrawSelector);
            List<Card> deck = [Trade(copies: 2, opt: opt), returnCard,
                new([Monster, Level, Role], id: "a"), new(membership, id: "b"),
                new([Orcust, Monster], id: "draw", drawCount: 1, postDrawDiscardCount: 1), new([], id: "f")];
            List<Combo> combos = [new([new(Role, 1, 3)], groupId: "g"),
                new([], cards: [new("trade", 1, 2)], groupId: "g"),
                new([new(Level, 0, 0)]), new([], cards: [new("b", 1, 1)]),
                new([], alternativeGroups: [new([ComboAlternative.For(ComboCategory.SameCard([Monster, Orcust], 1, 2)),
                    ComboAlternative.For(ComboCategory.SameCard([Monster, Legacy], 1, 2))])])];
            AssertParity(deck, 2, combos, [new("g", "Group"), new("empty", "Empty")]);
        }
    }

    [Test]
    public void PreDrawPaymentIsAtomicExcludesSourceAndRetainsUnpayableCards() {
        Card source = Trade().WithCategories([Level, Monster]);
        Card material = new([Level, Monster], id: "material");
        Card reward = new([], id: "reward");
        AssertHand([source, reward], 1, [], "trade");
        AssertHand([source, material, reward], 2, [], "trade", "material");
        AssertHand([source, material], 2, [], "trade", "material");
        AssertHand([source.WithPreDrawRemovalCount(2), material, reward], 2, [], "trade", "material");
        AssertHand([source, source, reward], 2, [], "trade", "trade");
        AssertParity([source.WithCopies(2), material, reward], 2, [new([], cards: [new("trade", 1, 2)])], []);
    }

    [Test]
    public void TradeInWithOneCardRemainingRetainsItsSourceAndCost() {
        Card source = Trade(opt: true);
        Card material = new([Level], id: "material");
        List<Card> deck = [source, material, new([], id: "last")];
        List<Combo> combos = [new([], cards: [new("trade", 1, 1), new("material", 1, 1)])];
        DrawSequenceOracle.Counts oracle = DrawSequenceOracle.Enumerate(deck, 2, combos, []);

        Assert.That(DrawSequenceOracle.Resolve(deck, 2, combos)!.Select(card => card.Id),
            Is.EquivalentTo(new[] { "trade", "material" }));
        Assert.That(oracle.Total, Is.EqualTo(new BigInteger(6)));
        Assert.That(oracle.Wins[0], Is.EqualTo(new BigInteger(2)));
        Assert.That(oracle.Failed, Is.EqualTo(BigInteger.Zero));
        AssertParity(deck, 2, combos, []);
        AssertBackgroundWireMatchesOracle(deck, 2, combos);

        ExactCalculationResult basic = new DrawEffectCalculation(deck, 2, new(CalculationWorkPolicy.Interactive))
            .Calculate(combos, []);
        Assert.That(basic.Combos[0].Numerator * oracle.Total, Is.EqualTo(oracle.Wins[0] * basic.Combos[0].Denominator));
    }

    [Test]
    public void TradeInWithExactlyTwoCardsRemainingPaysCostAndDrawsBoth() {
        Card source = Trade(opt: true);
        Card material = new([Level], id: "material");
        Card first = new([], id: "first");
        Card second = new([], id: "second");
        List<Card> deck = [source, material, first, second];
        List<Combo> combos = [new([], cards: [new("first", 1, 1), new("second", 1, 1)])];
        DrawSequenceOracle.Counts oracle = DrawSequenceOracle.Enumerate(deck, 2, combos, []);

        Assert.That(DrawSequenceOracle.Resolve(deck, 2, combos)!.Select(card => card.Id),
            Is.EquivalentTo(new[] { "first", "second" }));
        Assert.That(oracle.Total, Is.EqualTo(new BigInteger(24)));
        Assert.That(oracle.Wins[0], Is.EqualTo(new BigInteger(8)));
        AssertParity(deck, 2, combos, []);
        AssertBackgroundWireMatchesOracle(deck, 2, combos);
    }

    [Test]
    public void OrcustratedReturnWithOneCardRemainingDoesNotPayItsSameCardCost() {
        Card source = new([], name: "Orcustrated Return", id: "return", externalCardId: 26845680,
            drawCount: 2, preDrawRemovalCount: 1,
            preDrawSelector: CardSelector.AnyOfSelectors([
                CardSelector.AllOf([Orcust.Identity, Monster.Identity]),
                CardSelector.AllOf([Legacy.Identity, Monster.Identity])
            ]));
        Card material = new([Orcust, Monster], id: "material");
        List<Card> deck = [source, material, new([], id: "last")];
        List<Combo> combos = [new([], cards: [new("return", 1, 1), new("material", 1, 1)])];
        DrawSequenceOracle.Counts oracle = DrawSequenceOracle.Enumerate(deck, 2, combos, []);

        Assert.That(DrawSequenceOracle.Resolve(deck, 2, combos)!.Select(card => card.Id),
            Is.EquivalentTo(new[] { "return", "material" }));
        Assert.That(oracle.Wins[0], Is.EqualTo(new BigInteger(2)));
        Assert.That(oracle.Failed, Is.EqualTo(BigInteger.Zero));
        AssertParity(deck, 2, combos, []);
    }

    [Test]
    public void CostRankingDoesNotProtectADrawWhoseCompleteBatchNoLongerFits() {
        Card source = Trade();
        Card firstDraw = new([], id: "first-draw", drawCount: 2, drawOncePerTurn: false);
        Card unusableDraw = new([Level], id: "z-unusable", drawCount: 3);
        Card material = new([Level], id: "material");
        List<Card> deck = [source, firstDraw, material, unusableDraw, new([], id: "first"), new([], id: "second")];
        List<Combo> combos = [new([], cards: [new("z-unusable", 0, 0)])];

        Assert.That(DrawSequenceOracle.Resolve(deck, 2, combos)!.Select(card => card.Id),
            Is.EquivalentTo(new[] { "material", "first", "second" }));
        AssertParity(deck, 2, combos, []);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DeferredCostSourceChecksBatchAgainAfterAnotherEffect(bool enoughRemaining) {
        Card source = Trade(opt: true);
        Card draw = new([], id: "draw", drawCount: 1, drawOncePerTurn: false);
        Card material = new([Level], id: "material");
        List<Card> deck = enoughRemaining
            ? [source, draw, material, new([], id: "first"), new([], id: "second")]
            : [source, draw, material, new([], id: "last")];
        List<Combo> combos = enoughRemaining
            ? [new([], cards: [new("first", 1, 1), new("second", 1, 1)])]
            : [new([], cards: [new("trade", 1, 1), new("material", 1, 1)])];
        int handSize = 2;

        string[] expected = enoughRemaining ? ["first", "second"] : ["trade", "material"];
        Assert.That(DrawSequenceOracle.Resolve(deck, handSize, combos)!.Select(card => card.Id), Is.EquivalentTo(expected));
        AssertParity(deck, handSize, combos, []);
    }

    [Test]
    public void PreDrawDeferredOptRetriesAfterDrawAndStopsWithoutProgress() {
        Card source = Trade(opt: true);
        Card draw = new([], id: "draw", drawCount: 1);
        Card material = new([Level, Monster], id: "material");
        Card reward = new([], id: "reward");
        AssertHand([source, draw, material, reward, source], 2, [], "reward", "trade");
        AssertHand([source, source, reward], 2, [], "trade", "trade");
        AssertHand([source, Trade("other", opt: true), draw, reward], 3, [], "trade", "other", "reward");
        AssertParity([source.WithCopies(2), Trade("other", opt: true), draw, material, reward], 3,
            [new([], cards: [new("trade", 1, 2)]), new([], cards: [new("other", 1, 1)])], []);
    }

    [Test]
    public void PreDrawSelectionProtectsPositiveAndUsableCardsAndRemovesQueuedCopies() {
        Card source = Trade();
        Card piece = new([Level, Monster, Role], id: "piece");
        Card spare = new([Level, Monster], id: "spare");
        Card draw = new([Level, Monster], id: "draw", drawCount: 1);
        Card blocked = Trade("blocked").WithCategories([Level, Monster]).WithPreDrawRemovalCount(2);
        Card reward = new([], id: "reward");
        Card last = new([], id: "last");
        List<Combo> combos = [new([new(Role, 1, 3)])];
        AssertHand([source, piece, spare, reward, last], 3, combos, "piece", "reward", "last");
        AssertHand([source, draw, spare, reward, last, piece], 3, [], "reward", "last", "piece");
        AssertHand([source, blocked, reward, last, piece], 2, [], "reward", "last");
        AssertHand([source, draw, reward, last, piece], 2, [], "reward", "last");
        AssertParity([source, piece, spare, draw, blocked, reward], 3, combos, []);
        // Maximum-only references never give positive protection.
        AssertParity([source, piece, spare, reward], 3, [new([new(Role, 0, 0)])], []);
    }

    [TestCase(1), TestCase(2), TestCase(3), TestCase(5)]
    public void PreDrawAndPostDrawCombinationHandlesExhaustionAndCandidateRemoval(int hand) {
        List<Card> deck = [Trade().WithPostDrawDiscardCount(1), Trade("other").WithCategories([Level, Monster]),
            new([Level, Monster], 2, id: "material"), new([], id: "target"), new([], id: "f")];
        AssertParity(deck, hand, [new([], cards: [new("other", 1, 1)]), new([], cards: [new("target", 1, 1)])], []);
    }

    [Test]
    public void PreDrawReductionKeepsDifferentFutureEligibilityAndCanonicalTies() {
        List<Card> deck = [Trade(), new([Monster, Level], id: "a"), new([Level], id: "b"),
            new([Monster, Level], id: "c"), new([Monster], id: "d"), new([Role], id: "target")];
        AssertParity(deck, 2, [new([new(Role, 1, 1)])], []);
    }

    [Test]
    public void PreDrawModeAndWireParityUseExactEnumeratedSmallExample() {
        List<Card> deck = [Trade(), new([Monster, Level], id: "material"), new([], id: "target"), new([], id: "f")];
        List<Combo> combos = [new([], cards: [new("target", 1, 1)])];
        foreach (bool global in new[] { false, true }) {
            foreach (bool advanced in new[] { false, true }) {
                List<Card> modeled = deck.Select(card => !global ? card.WithDrawCount(null)
                    : !advanced ? card.WithPreDrawRemovalCount(0) : card).ToList();
                DrawSequenceOracle.Counts oracle = DrawSequenceOracle.Enumerate(modeled, 2, combos, []);
                CalculationSnapshot snapshot = CalculationSnapshot.Capture(modeled, combos, 2, [], drawEffectsEnabled: global, options: new());
                double probability = CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json)).TotalProbability;
                Assert.That(probability, Is.EqualTo((double)oracle.Wins[0] / (double)oracle.Total));
                TestContext.Out.WriteLine($"global={global}, advanced={advanced}: {oracle.Wins[0]}/{oracle.Total}");
            }
        }
        WorkBudget costBasic = new(CalculationWorkPolicy.Interactive);
        WorkBudget plainBasic = new(CalculationWorkPolicy.Interactive);
        new DrawEffectCalculation(deck.Select(card => card.WithPreDrawRemovalCount(0)).ToList(), 2, costBasic).Calculate(combos, []);
        new DrawEffectCalculation(deck.Select(card => card.WithPreDrawRemovalCount(0)).ToList(), 2, plainBasic).Calculate(combos, []);
        Assert.That(costBasic.Spent, Is.EqualTo(plainBasic.Spent));
    }

    [Test]
    public void PreDrawResourceFailuresRemainTypedAndReturnNoPartialResult() {
        List<Card> deck = [Trade(copies: 3), new([Monster, Level], 12, id: "material"), new([Role], 9, id: "target"), new([], 16, id: "f")];
        List<Combo> combos = [new([new(Role, 1, 3)])];
        CalculationSnapshot snapshot = CalculationSnapshot.Capture(deck, combos, 5, [], new(10), options: new());
        ProbabilityCalculationLimitException? error = Assert.Throws<ProbabilityCalculationLimitException>(() => CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json)));
        Assert.That(error!.Reason, Is.EqualTo(ProbabilityCalculationLimitReason.Work));
        // Many distinct direct roles force a storage-bound exact frontier.
        List<Card> distinct = Enumerable.Range(0, 24).Select(i => new Card([Monster, Level], id: $"c{i}")).ToList();
        distinct.Add(Trade(copies: 3));
        Combo all = new([], cards: distinct.Select(card => new ComboCard(card.Id, 0, 1)).ToList());
        WorkBudget budget = new(new(500_000_000));
        error = Assert.Throws<ProbabilityCalculationLimitException>(() => new PostDrawCalculation(distinct, 5, budget).Calculate([all], []));
        Assert.That(error!.Reason, Is.EqualTo(ProbabilityCalculationLimitReason.Storage));
    }

    private static void AssertHand(Card[] order, int hand, List<Combo> combos, params string[] expected) =>
        Assert.That(DrawSequenceOracle.Resolve(order, hand, combos)!.Select(card => card.Id), Is.EquivalentTo(expected));

    internal static void AssertParity(List<Card> deck, int hand, List<Combo> combos, List<ComboGroup> groups) {
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
        TestContext.Out.WriteLine($"Exact parity: {oracle.Total} labeled orders; {oracle.Wins.Length} result rows; hand {hand}.");
    }

    private static void AssertBackgroundWireMatchesOracle(List<Card> deck, int handSize, List<Combo> combos) {
        foreach (bool drawEffectsEnabled in new[] { false, true }) {
            foreach (bool advanced in new[] { false, true }) {
                List<Card> modeled = deck.Select(card => !drawEffectsEnabled
                    ? card.WithDrawCount(null).WithPreDrawRemovalCount(0).WithPostDrawDiscardCount(0)
                    : !advanced ? card.WithPreDrawRemovalCount(0).WithPostDrawDiscardCount(0) : card).ToList();
                DrawSequenceOracle.Counts oracle = DrawSequenceOracle.Enumerate(modeled, handSize, combos, []);
                CalculationSnapshot snapshot = CalculationSnapshot.Capture(modeled, combos, handSize, [],
                    drawEffectsEnabled: drawEffectsEnabled, options: new());
                double actual = CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json)).TotalProbability;
                double expected = (double)oracle.Wins[0] / (double)oracle.Total;

                Assert.That(actual, Is.EqualTo(expected).Within(1e-12), $"Draw effects {drawEffectsEnabled}, Advanced {advanced}");
            }
        }
    }
}
