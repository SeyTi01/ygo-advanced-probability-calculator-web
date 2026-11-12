using CardProbability.EngineTests.Fixtures;
using CardProbability.Internal;

namespace CardProbability.EngineTests;

[TestFixture]
internal sealed class DrawAssessmentNeutralTest {
    [Test]
    public void SearchHandOperationsDoNotClaimNoSearchExchangeability() {
        List<DeckEntry> cards = [new("draw", 1, new(DrawCount: 2, ActivationLimit: null, RemovalCount: 1)),
            new("target", 3), new("filler", 5)];
        Assert.That(Assess(cards, 2).Path, Is.EqualTo(DrawExecutionPath.ExchangeableConditional));
        cards.Add(new("search", 1, new(Search: new(new("role")))));
        Assert.That(Assess(cards, 2), Is.EqualTo(new DrawExecutionAssessment(DrawExecutionPath.OrderedFrontier, false)));
        cards[0] = new("draw", 1, new(DrawCount: 2, ActivationLimit: null));
        Assert.That(Assess(cards, 2).Path, Is.EqualTo(DrawExecutionPath.DrawCountState));
    }

    [Test]
    public void FilteredAndExhaustedEffectsAreNotAutomaticallyHighRisk() {
        List<DeckEntry> cards = [new("draw", 1, new(DrawCount: 2, RemovalCount: 1, RemovalEligibility: new("role"))),
            new("target", 3), new("filler", 2)];
        Assert.That(Assess(cards, 2), Is.EqualTo(new DrawExecutionAssessment(DrawExecutionPath.OrderedFrontier, false)));
        Assert.That(Assess(cards, 5), Is.EqualTo(new DrawExecutionAssessment(DrawExecutionPath.Ordinary, false)));
        cards[0] = new("draw", 0);
        Assert.That(Assess(cards, 2), Is.EqualTo(new DrawExecutionAssessment(DrawExecutionPath.Ordinary, false)));
    }

    [Test]
    public void SpecializationProofIsConservativeAndHandSizeSensitive() {
        List<DeckEntry> cards = [new("draw", 3, new(DrawCount: 3, ActivationLimit: null, RemovalCount: 2)),
            new("target", 20), new("filler", 17)];
        Assert.That(Assess(cards, 5).Path, Is.EqualTo(DrawExecutionPath.ExchangeableConditional));
        Assert.That(Assess(cards, 35).Path, Is.EqualTo(DrawExecutionPath.OrderedFrontier));
        cards.AddRange(Enumerable.Range(0, 130).Select(index => new DeckEntry($"extra-{index}", 1)));
        Assert.That(Assess(cards, 5).Path, Is.EqualTo(DrawExecutionPath.Unknown));
    }

    [Test]
    public void ExcavationRiskRequiresReachableExcavationHiddenDrawAndSearch() {
        List<DeckEntry> cards = [new("excavate", 1, new(Excavation: new(3))),
            new("draw", 1, new(DrawCount: 2, HiddenDeckCost: 1)), new("search", 1, new(Search: new(new("role")))),
            new("target", 3), new("a", 3), new("b", 3), new("c", 3), new("filler", 25)];
        Goal[] goals = Enumerable.Range(0, 4).Select(index => new Goal($"g{index}", [Requirement.InMembership("role")])).ToArray();
        DrawExecutionAssessment Run(int hand) {
            EngineCase fixture = new("excavation", new(cards, hand, goals, [new("role", ["target"])]), new());
            PreparedCalculation model = fixture.Prepare();
            return DrawExecutionAssessment.Assess(model.Cards, model.Combos, hand, true);
        }
        Assert.That(Run(5).ElevatedRisk, Is.True);
        cards[1] = new("draw", 1, new(DrawCount: 2, HiddenPostDrawMill: 10));
        Assert.That(Run(5).ElevatedRisk, Is.True);
        Assert.That(Run(35).ElevatedRisk, Is.False);
        cards[2] = new("search", 1);
        Assert.That(Run(5).ElevatedRisk, Is.False);
    }

    [Test]
    public void DeferredRouteWarnsWhenManyObservedIdentitiesRemain() {
        List<DeckEntry> cards = Enumerable.Range(0, 10).Select(index => new DeckEntry($"dark-{index}", 1)).ToList();
        cards.AddRange([new("light-a", 1), new("light-b", 1),
            new("draw", 1, new(DrawCount: 2, RemovalCount: 1, RemovalEligibility: new("dark")))]);
        Goal[] goals = cards.Take(12).Select(entry => new Goal(entry.Id, [Requirement.OfEntry(entry.Id, 1, 1)])).ToArray();
        EngineCase fixture = new("observed", new(cards, 5, goals,
            [new("dark", cards.Take(10).Select(entry => entry.Id))]), new());
        PreparedCalculation model = fixture.Prepare();
        Assert.That(DrawExecutionAssessment.Assess(model.Cards, model.Combos, 5, true),
            Is.EqualTo(new DrawExecutionAssessment(DrawExecutionPath.DeferredConditional, true)));
    }
    private static DrawExecutionAssessment Assess(List<DeckEntry> cards, int hand) {
        EngineCase fixture = new("assessment", new(cards, hand, [new("goal", [Requirement.InMembership("role", 1, 5)])],
            [new("role", ["target"])]), new());
        PreparedCalculation model = fixture.Prepare();
        return DrawExecutionAssessment.Assess(model.Cards, model.Combos, hand, true);
    }
}
