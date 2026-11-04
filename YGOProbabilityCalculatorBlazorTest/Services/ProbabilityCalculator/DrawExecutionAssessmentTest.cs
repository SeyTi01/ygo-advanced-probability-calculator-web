using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public class DrawExecutionAssessmentTest {
    [Test]
    public void ExcavationBenchmarksWarnForTheExpensiveHiddenSearchCombination() {
        foreach ((string name, SessionState session) in ExcavationBenchmark.Fixtures()) {
            Assert.That(Assess(session).ElevatedRisk, Is.EqualTo(name == "40-duality-hidden-search"), name);
        }
    }

    [Test]
    public void ExcavationWarningIncludesHiddenMillsButExcludesUnpayableDraws() {
        SessionState session = ExcavationBenchmark.Fixtures().Single(fixture => fixture.Name == "40-duality-hidden-search").Session;
        int index = session.Cards.FindIndex(card => card.Id == "draw");
        session.Cards[index] = session.Cards[index].WithPreDrawDeckRemovalCount(0).WithPostDrawDeckMillCount(10);
        Assert.That(Assess(session).ElevatedRisk, Is.True);
        Assert.That(DrawExecutionAssessment.Assess(session.Cards, session.Combos, 35, true).ElevatedRisk, Is.False);
    }

    [TestCase("pot")]
    [TestCase("draw")]
    [TestCase("search-a")]
    public void ExcavationWarningRequiresActiveEnabledEffects(string source) {
        SessionState session = ExcavationBenchmark.Fixtures().Single(fixture => fixture.Name == "40-duality-hidden-search").Session;
        if (source == "search-a") {
            session.Cards.RemoveAll(card => card.Id == "search-any");
        }
        int index = session.Cards.FindIndex(card => card.Id == source);
        Card original = session.Cards[index];
        foreach (Card disabled in new[] { original.WithSimulationEnabled(false), original.WithActive(false), original.WithCopies(0) }) {
            session.Cards[index] = disabled;
            Assert.That(Assess(session).ElevatedRisk, Is.False);
        }
        session.Cards[index] = original;
        Assert.That(Assess(session).ElevatedRisk, Is.True);
        Assert.That(DrawExecutionAssessment.Assess(session.Cards, session.Combos, session.HandSize, false).ElevatedRisk, Is.False);
        session.Combos.RemoveRange(3, session.Combos.Count - 3);
        Assert.That(Assess(session).ElevatedRisk, Is.False);
    }

    [Test]
    public void SearchHandOperationsDoNotClaimNoSearchExchangeability() {
        CategoryBase role = new("Role");
        List<Card> cards = [new([], id: "draw", drawCount: 2, drawOncePerTurn: false, postDrawDiscardCount: 1),
            new([role], 3, id: "target"), new([], 5, id: "filler")];
        List<Combo> combos = [new([new(role, 1, 3)])];
        Assert.That(DrawExecutionAssessment.Assess(cards, combos, 2, true).Path, Is.EqualTo(DrawExecutionPath.ExchangeableConditional));
        cards.Add(new Card([], id: "search").WithSearch(new(CardSelector.For(role), false)));
        Assert.That(DrawExecutionAssessment.Assess(cards, combos, 2, true),
            Is.EqualTo(new DrawExecutionAssessment(DrawExecutionPath.OrderedFrontier, false)));
        cards[0] = cards[0].WithPostDrawDiscardCount(0);
        Assert.That(DrawExecutionAssessment.Assess(cards, combos, 2, true).Path, Is.EqualTo(DrawExecutionPath.DrawCountState));
    }

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
        Assert.That(DrawExecutionAssessment.Assess(session.Cards.Select(card => card.WithPostDrawDiscardCount(0)).ToList(), session.Combos, 5, true),
            Is.EqualTo(new DrawExecutionAssessment(DrawExecutionPath.DrawCountState, false)));
        Assert.That(DrawExecutionAssessment.Assess(session.Cards, session.Combos, 5, false),
            Is.EqualTo(new DrawExecutionAssessment(DrawExecutionPath.Ordinary, false)));
    }

    [Test]
    public void SimpleFilteredAndExhaustionSensitiveEffectsAreNotAutomaticallyHighRisk() {
        CategoryBase role = new("Role");
        List<Card> cards = [new([], id: "draw", drawCount: 2, postDrawDiscardCount: 1, postDrawSelector: CardSelector.For(role)),
            new([role], 3, id: "target"), new([], 2, id: "filler")];
        List<Combo> combos = [new([new(role, 1, 3)])];
        Assert.That(DrawExecutionAssessment.Assess(cards, combos, 2, true),
            Is.EqualTo(new DrawExecutionAssessment(DrawExecutionPath.OrderedFrontier, false)));
        Assert.That(DrawExecutionAssessment.Assess(cards, combos, 5, true),
            Is.EqualTo(new DrawExecutionAssessment(DrawExecutionPath.Ordinary, false)));
        cards[0] = cards[0].WithActive(false);
        Assert.That(DrawExecutionAssessment.Assess(cards, combos, 2, true),
            Is.EqualTo(new DrawExecutionAssessment(DrawExecutionPath.Ordinary, false)));
    }

    [Test]
    public void SpecializationProofIsConservativeAndHandSizeSensitive() {
        CategoryBase role = new("Role");
        List<Card> cards = [new([], 3, id: "draw", drawCount: 3, drawOncePerTurn: false, postDrawDiscardCount: 2),
            new([role], 20, id: "target"), new([], 17, id: "filler")];
        List<Combo> combos = [new([new(role, 1, 5)])];
        Assert.That(DrawExecutionAssessment.Assess(cards, combos, 5, true).Path, Is.EqualTo(DrawExecutionPath.ExchangeableConditional));
        Assert.That(DrawExecutionAssessment.Assess(cards, combos, 35, true).Path, Is.EqualTo(DrawExecutionPath.OrderedFrontier));
        CategoryBase unrelated = new("Unrelated");
        cards.Add(new([unrelated], id: "different-filler"));
        combos.Add(new([new(unrelated, 0, 0)]));
        Assert.That(DrawExecutionAssessment.Assess(cards, combos, 5, true),
            Is.EqualTo(new DrawExecutionAssessment(DrawExecutionPath.Unknown, false)));
        cards.AddRange(Enumerable.Range(0, 130).Select(index => new Card([], id: $"extra-{index}")));
        Assert.That(DrawExecutionAssessment.Assess(cards, combos, 5, true).Path, Is.EqualTo(DrawExecutionPath.Unknown));
    }

    [Test]
    public void DeferredRouteStillWarnsWhenManyObservedIdentitiesRemain() {
        CategoryBase dark = new("Dark");
        List<Card> cards = Enumerable.Range(0, 10).Select(index => new Card([dark], id: $"dark-{index}")).ToList();
        cards.AddRange([new([], id: "light-a"), new([], id: "light-b"),
            new([], id: "draw", drawCount: 2, postDrawDiscardCount: 1, postDrawSelector: CardSelector.For(dark))]);
        List<Combo> combos = cards.Take(12).Select(card => new Combo([], cards: [new(card.Id, 1, 1)])).ToList();
        Assert.That(DrawExecutionAssessment.Assess(cards, combos, 5, true),
            Is.EqualTo(new DrawExecutionAssessment(DrawExecutionPath.DeferredConditional, true)));
    }

    private static DrawExecutionAssessment Assess(SessionState session) =>
        DrawExecutionAssessment.Assess(session.Cards, session.Combos, session.HandSize, true);
}
