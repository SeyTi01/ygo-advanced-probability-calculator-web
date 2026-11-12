using CardProbability.EngineTests.Fixtures;
using CardProbability.Internal;
using CardProbability.Internal.Contracts;
using CardProbability.Internal.Counting;
using CardProbability.Internal.Resources;
using CardProbability.Internal.Search;
using DrawResolutionResult = CardProbability.Internal.DrawResolution.DrawResolution;

namespace CardProbability.EngineTests;

[TestFixture]
internal sealed class TargetSurvivalBudgetTest {
    [Test]
    public void OversizedScenarioAndIdentityProofsSkipWithoutSpendingTheirRequest() {
        DeckEntry[] entries = [new("source", 3, new(DrawCount: 2, HiddenDeckCost: 10)),
            new("target", 5), new("search", 32, new(ActivationLimit: null, Search: new(new("role"))))];
        Membership[] memberships = [new("role", ["target"])];
        EngineCase fixture = new("oversized", new(entries, 5,
            [new("goal", [Requirement.OfEntry("target", 5)])], memberships), new());
        PreparedCalculation prepared = fixture.Prepare();
        ExactCalculationResult primary = new(ExactProbability.One, [ExactProbability.One], []);
        DrawResolutionResult oversized = new(Enumerable.Range(0, 65).ToDictionary(
            count => new DrawComposition(count, [0, 0]), _ => ExactProbability.One));
        WorkBudget budget = new(CalculationWorkPolicy.Default);
        Assert.That(TargetSurvivalUpperBound.TryCalculate(prepared.Cards, [prepared.Cards[0]], oversized,
            prepared.Combos, [], primary, budget), Is.Null);
        Assert.That(budget.Spent, Is.Zero);
        Assert.That(budget.PeakCells, Is.Zero);

        EngineCase repeated = new("repeated", new(entries, 5,
            [new("goal", Enumerable.Repeat(Requirement.OfEntry("target", 5, 5), 5))], memberships), new());
        prepared = repeated.Prepare();
        DrawResolutionResult small = new(new() { [new(5, [0, 35])] = ExactProbability.One });
        Assert.That(TargetSurvivalUpperBound.TryCalculate(prepared.Cards, [prepared.Cards[0]], small,
            prepared.Combos, [], primary, budget), Is.Null);
        Assert.That(budget.Spent, Is.LessThan(100));
    }
}
