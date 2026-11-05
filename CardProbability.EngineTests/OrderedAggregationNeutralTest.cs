using System.Numerics;
using CardProbability.EngineTests.Fixtures;
using CardProbability.Internal;
using CardProbability.Internal.Compilation;
using CardProbability.Internal.Contracts;
using CardProbability.Internal.Counting;
using CardProbability.Internal.DrawResolution;
using CardProbability.Internal.Model;
using CardProbability.Internal.Resources;
using CardProbability.Internal.Search;

namespace CardProbability.EngineTests;

[TestFixture, NonParallelizable]
internal sealed class OrderedAggregationNeutralTest {
    [TestCase(false), TestCase(true)]
    public void TerminalMassAndCertificateRowsMatchUnaggregatedAndIndependentOracle(bool defer) {
        foreach (EngineCase fixture in SearchCertificateFixtures.Fixtures()) {
            PreparedCalculation model = fixture.Prepare();
            DrawActivationPriority priority = new(DrawActivationMode.CustomPriority, fixture.Options.PriorityEntryIds);
            Dictionary<string, BigInteger>[] mass = [new(), new()];
            ExactProbability[][][] rows = [new ExactProbability[3][], new ExactProbability[3][]];
            for (int side = 0; side < 2; side++) {
                int index = side;
                WorkBudget budget = new(CalculationWorkPolicy.Interactive);
                PostDrawCalculation calc = new(model.Cards, fixture.Request.HandSize, budget, priority, defer,
                    compactFrontierThreshold: 0, eagerTerminalAggregation: side == 1,
                    reuseSearchTemplates: side == 1, compactSearchTerminals: side == 1) {
                    StreamSearchTerminals = false,
                    ObserveTerminal = (hand, value) => {
                        string key = string.Join(',', hand);
                        mass[index][key] = mass[index].GetValueOrDefault(key) + value;
                        Array.Fill(hand, int.MinValue);
                    }
                };
                rows[side][0] = Rows(calc.CalculateSearch(model.Combos, model.Groups, false, new SearchAccessPlan(model.Cards, budget), coalesceOrdinary: defer));
                rows[side][1] = Rows(calc.CalculateSearchLower(model.Combos, model.Groups, true));
                rows[side][2] = Rows(calc.CalculateSearchLower(model.Combos, model.Groups, false));
                Assert.That(calc.PackedLayers, Is.GreaterThan(0), fixture.Name);
            }
            Assert.That(mass[1], Is.EquivalentTo(mass[0]), fixture.Name);
            for (int pass = 0; pass < 3; pass++) {
                Assert.That(rows[1][pass], Is.EqualTo(rows[0][pass]), fixture.Name);
            }
            SearchCertificateOracle.Counts truth = SearchCertificateOracle.Enumerate(fixture);
            for (int row = 0; row < rows[1][0].Length; row++) {
                Assert.That(rows[1][0][row].Numerator * truth.Outcomes, Is.EqualTo(truth.Wins[2][row] * rows[1][0][row].Denominator), fixture.Name);
                Assert.That(rows[1][1][row].Numerator * truth.Outcomes, Is.EqualTo(truth.Wins[4][row] * rows[1][1][row].Denominator), fixture.Name);
                Assert.That(rows[1][2][row].Numerator * truth.Outcomes, Is.LessThanOrEqualTo(truth.Wins[1][row] * rows[1][2][row].Denominator), fixture.Name);
            }
        }
    }

    [TestCase(255), TestCase(256)]
    public void TerminalEncodingHonorsByteCapacityAndWideFallback(int copies) {
        EngineCase fixture = new("terminal-width", new([
            new("draw", 1, new(DrawCount: 1, RemovalCount: 1)),
            new("search", 1, new(Search: new(new("target")))),
            new("target", 3), new("filler", copies - 5)
        ], 2, [new("none", [Requirement.InMembership("target", 0, 0)]),
            new("some", [Requirement.InMembership("target", 1, 2)])], [new("target", ["target"])]), new());
        PreparedCalculation model = fixture.Prepare();
        WorkBudget[] budgets = [new(CalculationWorkPolicy.Interactive), new(CalculationWorkPolicy.Interactive)];
        ExactCalculationResult[] results = new ExactCalculationResult[2];
        for (int side = 0; side < 2; side++) {
            PostDrawCalculation calculation = new(model.Cards, 2, budgets[side], compactSearchTerminals: side == 1);
            results[side] = calculation.CalculateSearch(model.Combos, model.Groups, false, new SearchAccessPlan(model.Cards, budgets[side]));
        }
        Assert.That(Rows(results[1]), Is.EqualTo(Rows(results[0])));
        if (copies > byte.MaxValue) {
            Assert.That(budgets[1].Spent, Is.EqualTo(budgets[0].Spent));
            Assert.That(budgets[1].PeakCells, Is.EqualTo(budgets[0].PeakCells));
        }
    }
    [TestCase(0), TestCase(1), TestCase(31), TestCase(32), TestCase(63), TestCase(64), TestCase(65), TestCase(96)]
    public void SparseProjectionMatchesFullScanAcrossMachineWordBoundary(int retainedRows) {
        List<Card> retained = Enumerable.Range(0, retainedRows).Select(index =>
            new Card($"r{index}", index % 3, [], new())).ToList();
        BigInteger all = (BigInteger.One << (retainedRows + 2)) - 1;
        foreach (int maximum in new[] { 0, 1, 3, 200 }) {
            CompiledEvent compiled = new([new(all, 1, maximum), new(all ^ 1, 0, 200)]);
            ScenarioConditioner full = new(2, 3, retained, new WorkBudget(CalculationWorkPolicy.Interactive));
            ScenarioConditioner sparse = new(2, 3, retained, new WorkBudget(CalculationWorkPolicy.Interactive), sparseFixedRows: true);
            Assert.That(sparse.Project(compiled), Is.EqualTo(full.Project(compiled)));
        }
    }
    private static ExactProbability[] Rows(ExactCalculationResult result) => [result.Total, .. result.Combos, .. result.Groups];
}
