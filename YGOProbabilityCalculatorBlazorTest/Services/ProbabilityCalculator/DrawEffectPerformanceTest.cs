using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public class DrawEffectPerformanceTest {
    [TestCase(1475311, true)]
    [TestCase(38120068, true)]
    [TestCase(26845680, true)]
    [TestCase(79571449, false)]
    [TestCase(35261759, false)]
    [TestCase(55144522, false)]
    public void BuiltInsAndEquivalentOverridesUseEffectiveMechanics(int identity, bool expected) {
        Card card = CardEffectRegistry.Default.RestoreBuiltIn(new([], externalCardId: identity));
        Assert.That(NeedsDetail(card), Is.EqualTo(expected));
        Card custom = card.WithEffect(card.EffectPlan, card.EffectProfile.AsCustom()).WithName("Unrelated name").WithActive(false).WithCopies(0);
        Assert.That(NeedsDetail(custom), Is.EqualTo(expected));
        Assert.That(NeedsDetail(custom.WithEffect(custom.EffectPlan, EffectProfile.Legacy)), Is.EqualTo(expected));
        Assert.That(NeedsDetail(custom.WithDrawCount(null)), Is.False);
        Assert.That(NeedsDetail(custom.WithEffect(custom.EffectPlan, custom.EffectProfile with { ExecutionModel = EffectExecutionModel.Unsupported })), Is.False);
    }

    [Test]
    public void SpecializationShapeBoundariesAreIntrinsicButCapacityAndRelevanceAreNot() {
        Card graceful = new([], 3, id: "effect", drawCount: 3, drawOncePerTurn: false, postDrawDiscardCount: 2);
        CategoryBase role = new("Role");
        List<Card> cards = [graceful, new([role], 20), new([], 17)];
        List<Combo> routes = [new([new(role, 1, 5)])];
        Assert.That(NeedsDetail(graceful), Is.False);
        Assert.That(Assess(cards, routes, 5).Path, Is.EqualTo(DrawExecutionPath.ExchangeableConditional));
        Assert.That(Assess(cards, routes, 35).Path, Is.EqualTo(DrawExecutionPath.OrderedFrontier));
        routes.Add(new([], cards: [new(graceful.Id, 1, 3)]));
        Assert.That(Assess(cards, routes, 5).Path, Is.EqualTo(DrawExecutionPath.OrderedFrontier));
        Assert.That(NeedsDetail(graceful), Is.False);

        Assert.That(NeedsDetail(graceful.WithDrawOncePerTurn(true)), Is.True);
        Assert.That(NeedsDetail(graceful.WithDrawCount(2)), Is.True);
        Assert.That(NeedsDetail(graceful.WithPostDrawSelector(CardSelector.For(role))), Is.True);
        Assert.That(NeedsDetail(graceful.WithPreDrawRemovalCount(1)), Is.True);
        Assert.That(NeedsDetail(graceful.WithPreDrawDeckRemovalCount(10)), Is.False);
        Assert.That(NeedsDetail(new([], drawCount: 2)), Is.False);
        Assert.That(NeedsDetail(new([])), Is.False);
    }

    [Test]
    public void DesiresCapacityAndUnknownProofDoNotCreateIntrinsicWarnings() {
        Card desires = CardEffectRegistry.Default.RestoreBuiltIn(new([], externalCardId: 35261759));
        Assert.That(Assess([desires, new([], 39)], [], 5).Path, Is.EqualTo(DrawExecutionPath.DrawCountState));
        Assert.That(Assess([desires, new([], 11)], [], 5).Path, Is.EqualTo(DrawExecutionPath.Ordinary));
        Assert.That(NeedsDetail(desires), Is.False);
        CategoryBase excluded = new("Excluded");
        Card graceful = new([], 3, drawCount: 3, drawOncePerTurn: false, postDrawDiscardCount: 2);
        Assert.That(Assess([graceful, new([], 36), new([excluded])], [new([new(excluded, 0, 0)])], 5),
            Is.EqualTo(new DrawExecutionAssessment(DrawExecutionPath.Unknown, false)));
        Assert.That(NeedsDetail(graceful), Is.False);
    }

    [Test]
    public async Task MixedProxyAndCombinationOnlyCostKeepAttributionSeparateFromRisk() {
        SessionState session = await RealisticGracefulFixture.Load();
        Card graceful = session.Cards.Single(card => card.Name == "Graceful Charity");
        Assert.That(Assess(session.Cards, session.Combos, 5), Is.EqualTo(new DrawExecutionAssessment(DrawExecutionPath.ExchangeableConditional, false)));
        Assert.That(NeedsDetail(graceful), Is.False);
        Card greed = CardEffectRegistry.Default.RestoreBuiltIn(new([], 1, externalCardId: 55144522));
        session.Cards[session.Cards.IndexOf(graceful)] = graceful.WithCopies(2);
        session.Cards.Add(greed);
        Assert.That(Assess(session.Cards, session.Combos, 5), Is.EqualTo(new DrawExecutionAssessment(DrawExecutionPath.OrderedFrontier, true)));
        Assert.That(session.Cards.Any(NeedsDetail), Is.False, "combination cost does not imply an intrinsically costly card");
        session.Cards[session.Cards.IndexOf(greed)] = CardEffectRegistry.Default.RestoreBuiltIn(new([], 1, externalCardId: 35261759));
        Card allure = CardEffectRegistry.Default.RestoreBuiltIn(new([], 1, externalCardId: 1475311));
        session.Cards[session.Cards.FindIndex(card => card.Id == graceful.Id)] = allure;
        Assert.That(Assess(session.Cards, session.Combos, 5), Is.EqualTo(new DrawExecutionAssessment(DrawExecutionPath.DeferredConditional, false)));
        Assert.That(session.Cards.Where(NeedsDetail), Is.EqualTo(new[] { allure }));
        Assert.That(Assess([allure, new([], 39)], [], 5), Is.EqualTo(new DrawExecutionAssessment(DrawExecutionPath.OrderedFrontier, false)));
    }

    private static bool NeedsDetail(Card card) => DrawEffectPerformance.RequiresDetailedHandState(card.EffectPlan, card.EffectProfile.ExecutionModel);
    private static DrawExecutionAssessment Assess(IReadOnlyList<Card> cards, IReadOnlyList<Combo> combos, int handSize) =>
        DrawExecutionAssessment.Assess(cards, combos, handSize, true);
}
