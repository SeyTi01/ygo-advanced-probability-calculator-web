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
    public void DeferredRouteStillWarnsWhenManyObservedIdentitiesRemain() {
        CategoryBase dark = new("Dark");
        List<Card> cards = Enumerable.Range(0, 10).Select(index => new Card([dark], id: $"dark-{index}")).ToList();
        cards.AddRange([new([], id: "light-a"), new([], id: "light-b"),
            new([], id: "draw", drawCount: 2, postDrawDiscardCount: 1, postDrawSelector: CardSelector.For(dark))]);
        List<Combo> combos = cards.Take(12).Select(card => new Combo([], cards: [new(card.Id, 1, 1)])).ToList();
        Assert.That(DrawExecutionAssessment.Assess(cards, combos, 5, true).ElevatedRisk, Is.True);
    }

    private static DrawExecutionAssessment Assess(SessionState session) =>
        DrawExecutionAssessment.Assess(session.Cards, session.Combos, session.HandSize, true);
}
