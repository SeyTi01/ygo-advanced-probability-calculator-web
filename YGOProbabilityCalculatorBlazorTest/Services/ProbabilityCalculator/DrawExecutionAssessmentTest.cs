using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public class DrawExecutionAssessmentTest {
    [Test]
    public async Task BundledGracefulIsSpecializedAndSafePotAllureUsesDeferredCounting() {
        SessionState session = await RealisticGracefulFixture.Load();
        Assert.That(Assess(session), Is.EqualTo(new DrawExecutionAssessment(DrawExecutionPath.ExchangeableConditional, false)));
        Card graceful = session.Cards.Single(card => card.Name == "Graceful Charity");
        session.Cards[session.Cards.IndexOf(graceful)] = new([], 1, "Pot of Desires", id: graceful.Id, drawCount: 2);
        CategoryBase dark = new("DARK", CategorySource.Metadata, "attribute:dark");
        session.Cards.Add(new([], 1, "Allure of Darkness", id: "allure", drawCount: 2,
            postDrawDiscardCount: 1, postDrawSelector: CardSelector.For(dark)));
        session.Cards.Add(new([], 1, "Filler", id: "filler"));
        Assert.That(Assess(session), Is.EqualTo(new DrawExecutionAssessment(DrawExecutionPath.DeferredConditional, false)));
        Assert.That(DrawExecutionAssessment.Assess(session.Cards, session.Combos, 5, true, false),
            Is.EqualTo(new DrawExecutionAssessment(DrawExecutionPath.DrawCountState, false)));
        Assert.That(DrawExecutionAssessment.Assess(session.Cards, session.Combos, 5, false, false),
            Is.EqualTo(new DrawExecutionAssessment(DrawExecutionPath.Ordinary, false)));
    }

    [Test]
    public void SimpleFilteredAndExhaustionSensitiveEffectsAreNotAutomaticallyHighRisk() {
        CategoryBase role = new("Role");
        List<Card> cards = [new([], id: "draw", drawCount: 2, postDrawDiscardCount: 1, postDrawSelector: CardSelector.For(role)),
            new([role], 3, id: "target"), new([], 2, id: "filler")];
        List<Combo> combos = [new([new(role, 1, 3)])];
        Assert.That(DrawExecutionAssessment.Assess(cards, combos, 2, true, true),
            Is.EqualTo(new DrawExecutionAssessment(DrawExecutionPath.OrderedFrontier, false)));
        Assert.That(DrawExecutionAssessment.Assess(cards, combos, 5, true, false),
            Is.EqualTo(new DrawExecutionAssessment(DrawExecutionPath.Ordinary, false)));
        cards[0] = cards[0].WithActive(false);
        Assert.That(DrawExecutionAssessment.Assess(cards, combos, 2, true, true),
            Is.EqualTo(new DrawExecutionAssessment(DrawExecutionPath.Ordinary, false)));
    }

    [Test]
    public void SpecializationProofIsConservativeAndHandSizeSensitive() {
        CategoryBase role = new("Role");
        List<Card> cards = [new([], 3, id: "draw", drawCount: 3, drawOncePerTurn: false, postDrawDiscardCount: 2),
            new([role], 20, id: "target"), new([], 17, id: "filler")];
        List<Combo> combos = [new([new(role, 1, 5)])];
        Assert.That(DrawExecutionAssessment.Assess(cards, combos, 5, true, true).Path, Is.EqualTo(DrawExecutionPath.ExchangeableConditional));
        Assert.That(DrawExecutionAssessment.Assess(cards, combos, 35, true, true).Path, Is.EqualTo(DrawExecutionPath.OrderedFrontier));
        CategoryBase unrelated = new("Unrelated");
        cards.Add(new([unrelated], id: "different-filler"));
        combos.Add(new([new(unrelated, 0, 0)]));
        Assert.That(DrawExecutionAssessment.Assess(cards, combos, 5, true, true),
            Is.EqualTo(new DrawExecutionAssessment(DrawExecutionPath.Unknown, false)));
        cards.AddRange(Enumerable.Range(0, 130).Select(index => new Card([], id: $"extra-{index}")));
        Assert.That(DrawExecutionAssessment.Assess(cards, combos, 5, true, true).Path, Is.EqualTo(DrawExecutionPath.Unknown));
    }

    [Test]
    public void DeferredRouteStillWarnsWhenManyObservedIdentitiesRemain() {
        CategoryBase dark = new("Dark");
        List<Card> cards = Enumerable.Range(0, 10).Select(index => new Card([dark], id: $"dark-{index}")).ToList();
        cards.AddRange([new([], id: "light-a"), new([], id: "light-b"),
            new([], id: "draw", drawCount: 2, postDrawDiscardCount: 1, postDrawSelector: CardSelector.For(dark))]);
        List<Combo> combos = cards.Take(12).Select(card => new Combo([], cards: [new(card.Id, 1, 1)])).ToList();
        Assert.That(DrawExecutionAssessment.Assess(cards, combos, 5, true, true),
            Is.EqualTo(new DrawExecutionAssessment(DrawExecutionPath.DeferredConditional, true)));
    }

    private static DrawExecutionAssessment Assess(SessionState session) =>
        DrawExecutionAssessment.Assess(session.Cards, session.Combos, session.HandSize, true, true);
}
