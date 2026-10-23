using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Counting;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.DrawResolution;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public sealed class PhysicalTerminalMemoryTest {
    [TestCase(false), TestCase(true)]
    public void PackedPhysicalTerminalsPreserveEveryMassAndOracleRow(bool frontier) {
        PhysicalEnumerationMethod method = frontier ? PhysicalEnumerationMethod.Frontier : PhysicalEnumerationMethod.Auto;
        bool reduced = false;
        foreach (SearchCertificateFixtures.Fixture fixture in SearchCertificateFixtures.Fixtures()) {
            List<Card> cards = fixture.Deck.Where(card => card.Active && card.Copies > 0).ToList();
            DrawActivationPriority? priority = fixture.Priority is null ? null : new(DrawActivationMode.CustomPriority, fixture.Priority);
            Dictionary<string, BigInteger>[] terminals = [new(), new()];
            ExactCalculationResult[] results = new ExactCalculationResult[2];
            WorkBudget[] budgets = [new(CalculationWorkPolicy.Interactive), new(CalculationWorkPolicy.Interactive)];
            for (int side = 0; side < 2; side++) {
                int index = side;
                PostDrawCalculation calculation = new(cards, fixture.Hand, budgets[side], priority,
                    compactSearchTerminals: side == 1) {
                    ObserveTerminal = (values, mass) => {
                        terminals[index].Add(string.Join(',', values), mass);
                        Array.Fill(values, int.MinValue);
                    }
                };
                results[side] = calculation.CalculateSearch(fixture.Combos, fixture.Groups, false,
                    new(cards, budgets[side]), physical: true, method: method);
            }
            Assert.That(terminals[1], Is.EquivalentTo(terminals[0]), fixture.Name);
            Assert.That(results[1].Total, Is.EqualTo(results[0].Total), fixture.Name);
            Assert.That(results[1].Combos, Is.EqualTo(results[0].Combos), fixture.Name);
            Assert.That(results[1].Groups, Is.EqualTo(results[0].Groups), fixture.Name);
            SearchCertificateOracle.Counts oracle = SearchCertificateOracle.Enumerate(cards, fixture.Hand,
                fixture.Combos, fixture.Groups, fixture.Priority);
            ExactProbability[] rows = [results[1].Total, .. results[1].Combos, .. results[1].Groups];
            for (int row = 0; row < rows.Length; row++) {
                Assert.That(rows[row].Numerator * oracle.Outcomes,
                    Is.EqualTo(oracle.Wins[1][row] * rows[row].Denominator), fixture.Name);
            }
            reduced |= budgets[1].PeakCells < budgets[0].PeakCells;
        }
        Assert.That(reduced, Is.True, "At least one measured retained structure must actually shrink.");
    }
}
