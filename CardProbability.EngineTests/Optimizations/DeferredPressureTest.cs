using CardProbability.EngineTests.Fixtures;
using CardProbability.Internal;
using CardProbability.Internal.DrawResolution;
using CardProbability.Internal.Resources;
using Core = CardProbability.Internal.Contracts;

namespace CardProbability.EngineTests.Optimizations;

[TestFixture]
public sealed class DeferredPressureTest {
    [Test]
    public void ExchangeableRemovalRetainsCompactScheduleAndLargeTerminalDistribution() {
        EngineCase fixture = NeutralPressureFixtures.Load("exchangeable-removal");
        WorkBudget budget = new(Core.CalculationWorkPolicy.Interactive);
        PreparedCalculation input = fixture.Prepare(budget);
        PostDrawCalculation calculation = new(input.Cards, fixture.Request.HandSize, budget, DeferredTestCases.Priority(fixture));
        ExactCalculationResult result = calculation.Calculate(input.Combos, input.Groups);
        Assert.Multiple(() => {
            Assert.That(result.Combos, Has.Length.EqualTo(8));
            Assert.That(result.Groups, Has.Length.EqualTo(2));
            Assert.That(calculation.PeakStates, Is.LessThan(100));
            Assert.That(calculation.PeakCells, Is.LessThan(4096));
            Assert.That(calculation.TerminalStates, Is.GreaterThan(10_000));
            Assert.That(budget.Spent, Is.LessThan(Core.CalculationWorkPolicy.Interactive.WorkUnits));
        });
    }
}
