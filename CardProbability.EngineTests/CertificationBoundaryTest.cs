using System.Numerics;
using CardProbability;
using CardProbability.EngineTests.Fixtures;
using CardProbability.Internal;
using CardProbability.Internal.Certification;
using CardProbability.Internal.Counting;
using CardProbability.Internal.Resources;
using CardProbability.Internal.Search;
using Core = CardProbability.Internal.Contracts;
using M = CardProbability.Internal.Model;

namespace CardProbability.EngineTests;

[TestFixture]
public sealed class CertificationBoundaryTest {
    public static IEnumerable<TestCaseData> OracleCases() => SearchCertificateFixtures.Fixtures()
        .Select(fixture => new TestCaseData(fixture.Name).SetName("Certificate_" + fixture.Name));

    [TestCaseSource(nameof(OracleCases))]
    public void OptimisticAndRefinedRowsBoundEveryPhysicalOutcome(string name) {
        EngineCase fixture = SearchCertificateFixtures.Fixtures().Single(item => item.Name == name);
        WorkBudget budget = new(Core.CalculationWorkPolicy.Unlimited);
        PreparedCalculation prepared = fixture.Prepare(budget);
        DrawEffectCalculation execution = new(prepared.Cards, fixture.Request.HandSize, budget, Priority(fixture));
        ExactCalculationResult upper = execution.Calculate(prepared.Combos, prepared.Groups);
        Core.SearchAvailabilityCertificate certificate = SearchAvailabilityCertification.TryCalculate(
            prepared.Combos, prepared.Groups, upper, budget, execution);
        SearchCertificateOracle.Counts oracle = SearchCertificateOracle.Enumerate(fixture, CheckTerminal);
        ExactProbability[] primary = upper.ToRows();
        CalculationCapability capability = execution.DescribeCapability();
        Assert.That(certificate.Status, Is.AnyOf(Core.SearchAvailabilityStatus.Exact, Core.SearchAvailabilityStatus.Bounded), certificate.Detail);
        Core.SearchAvailabilityInterval[] intervals = [certificate.Total!, .. certificate.Combos, .. certificate.Groups];
        Assert.That(intervals, Has.Length.EqualTo(primary.Length));
        for (int row = 0; row < primary.Length; row++) {
            Assert.That(primary[row].Numerator * oracle.Outcomes, Is.EqualTo(oracle.Wins[2][row] * primary[row].Denominator), $"primary {row}");
            BigInteger lowerNumerator = BigInteger.Parse(intervals[row].Lower.Numerator);
            BigInteger lowerDenominator = BigInteger.Parse(intervals[row].Lower.Denominator);
            BigInteger upperNumerator = BigInteger.Parse(intervals[row].Upper.Numerator);
            BigInteger upperDenominator = BigInteger.Parse(intervals[row].Upper.Denominator);
            Assert.That(lowerNumerator * oracle.Outcomes, Is.LessThanOrEqualTo(oracle.Wins[1][row] * lowerDenominator), $"lower {row}");
            Assert.That(upperNumerator * oracle.Outcomes, Is.GreaterThanOrEqualTo(oracle.Wins[1][row] * upperDenominator), $"upper {row}");
            Assert.That(upperNumerator * primary[row].Denominator, Is.LessThanOrEqualTo(primary[row].Numerator * upperDenominator), $"refinement {row}");
            if (capability.Route == SolverRoute.SearchCountState) {
                Assert.That(lowerNumerator * oracle.Outcomes, Is.GreaterThanOrEqualTo(oracle.Wins[0][row] * lowerDenominator), $"elementary {row}");
            }
            if (capability.Accuracy == PhysicalSearchAccuracy.ProvenExact) {
                Assert.That(oracle.Wins[1][row], Is.EqualTo(oracle.Wins[2][row]), $"proven {row}");
            }
            if (certificate.Status == Core.SearchAvailabilityStatus.Exact) {
                Assert.That(intervals[row].IsExact, Is.True, $"exact {row}");
            }
        }

        void CheckTerminal(IReadOnlyList<DeckEntry> hand, int remaining, int lost, bool[] expected) {
            WorkBudget terminalBudget = new(Core.CalculationWorkPolicy.Unlimited);
            List<M.Card> retained = prepared.Cards.Select(card => card.WithCopies(hand.Count(entry => entry.Id == card.Id)))
                .Where(card => card.Copies > 0).ToList();
            SearchProbabilityCalculation lower = new([], 0, retained,
                new SearchAccessPlan(prepared.Cards, terminalBudget), remaining, terminalBudget, targetLoss: lost);
            ExactProbability[] rows = lower.Calculate(prepared.Combos, prepared.Groups, false).ToRows();
            for (int row = 0; row < rows.Length; row++) {
                Assert.That(rows[row].Numerator, Is.EqualTo(expected[row] ? rows[row].Denominator : BigInteger.Zero), $"terminal {row}");
            }
        }
    }

    [Test]
    public void OptionalWorkExhaustionPreservesTheCompletePrimaryWithoutPartialCertificate() {
        EngineCase fixture = SearchCertificateFixtures.Fixtures().First(item => item.Name.Contains("hidden-2"));
        WorkBudget measurement = new(Core.CalculationWorkPolicy.Unlimited);
        PreparedCalculation measured = fixture.Prepare();
        DrawEffectCalculation calculation = new(measured.Cards, fixture.Request.HandSize, measurement, Priority(fixture));
        ExactCalculationResult expected = calculation.Calculate(measured.Combos, measured.Groups);
        Core.ProbabilityCalculationResult expectedRows = expected.ToPublic(measured.Combos, measured.Groups, measurement);
        WorkBudget limited = new(new Core.CalculationWorkPolicy(measurement.Spent));
        PreparedCalculation prepared = fixture.Prepare();
        Core.ProbabilityCalculationResult actual = new CalculationRuntime().CalculateWithBudget(
            prepared.Cards, prepared.Combos, fixture.Request.HandSize, prepared.Groups, limited,
            new Core.CalculationOptions(Priority(fixture), CertifySearchAvailability: true));
        Assert.That(actual.TotalProbability, Is.EqualTo(expectedRows.TotalProbability));
        Assert.That(actual.ComboProbabilities, Is.EqualTo(expectedRows.ComboProbabilities));
        Assert.That(actual.GroupProbabilities, Is.EqualTo(expectedRows.GroupProbabilities));
        Assert.That(actual.SearchAvailability!.Status, Is.EqualTo(Core.SearchAvailabilityStatus.Limited));
        Assert.That(actual.SearchAvailability.Detail, Does.Contain("Work"));
        Assert.That(actual.SearchAvailability.Total, Is.Null);
        Assert.That(actual.SearchAvailability.Combos, Is.Empty);
        Assert.That(actual.SearchAvailability.Groups, Is.Empty);
    }

    [Test]
    public void OptionalStorageExhaustionPublishesNoPartialCertificate() {
        string[] groups = Enumerable.Range(0, 17_000).Select(index => $"g{index}").ToArray();
        EngineCase fixture = new("many-groups", new([new("t", 1), new("f", 1)], 1,
            [new("goal", [Requirement.OfEntry("t", 1, 1)])], groups: groups), new());
        PreparedCalculation prepared = fixture.Prepare();
        Core.ProbabilityCalculationResult result = new CalculationRuntime().CalculateWithBudget(
            prepared.Cards, prepared.Combos, 1, prepared.Groups, new(Core.CalculationWorkPolicy.Unlimited),
            new Core.CalculationOptions(CertifySearchAvailability: true));
        Assert.That(result.TotalProbability, Is.EqualTo(0.5));
        Assert.That(result.ComboProbabilities, Has.Count.EqualTo(1));
        Assert.That(result.GroupProbabilities, Has.Count.EqualTo(groups.Length));
        Assert.That(result.SearchAvailability!.Status, Is.EqualTo(Core.SearchAvailabilityStatus.Limited));
        Assert.That(result.SearchAvailability.Detail, Does.Contain("Storage"));
        Assert.That(result.SearchAvailability.Total, Is.Null);
        Assert.That(result.SearchAvailability.Combos, Is.Empty);
        Assert.That(result.SearchAvailability.Groups, Is.Empty);
    }

    [Test]
    public void CancellationEscapesOptionalProofAndUnexpectedErrorsAreReported() {
        PreparedCalculation prepared = SearchCertificateFixtures.Fixtures().First().Prepare();
        ExactCalculationResult incomplete = new(ExactProbability.Zero, [], []);
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();
        WorkBudget cancelledBudget = new(Core.CalculationWorkPolicy.Unlimited, cancellationToken: cancelled.Token);
        Assert.Throws<OperationCanceledException>(() => SearchAvailabilityCertification.TryCalculate(
            prepared.Combos, prepared.Groups, incomplete, cancelledBudget, null));
        Core.SearchAvailabilityCertificate error = SearchAvailabilityCertification.TryCalculate(
            prepared.Combos, prepared.Groups, incomplete, new(Core.CalculationWorkPolicy.Unlimited), null);
        Assert.That(error.Status, Is.EqualTo(Core.SearchAvailabilityStatus.Error));
        Assert.That(error.Detail, Does.Contain("IndexOutOfRangeException"));
        Assert.That(error.Total, Is.Null);
        Assert.That(error.Combos, Is.Empty);
        Assert.That(error.Groups, Is.Empty);
    }

    [Test]
    public void SecondaryProofErrorPreservesEveryCompletedPrimaryRow() {
        EngineCase fixture = SearchCertificateFixtures.Fixtures().First(item => item.Request.Goals.Length > 0);
        PreparedCalculation prepared = fixture.Prepare();
        WorkBudget budget = new(Core.CalculationWorkPolicy.Unlimited);
        ExactCalculationResult exact = new DrawEffectCalculation(prepared.Cards, fixture.Request.HandSize, budget)
            .Calculate(prepared.Combos, prepared.Groups);
        DrawEffectCalculation incomplete = new(prepared.Cards, fixture.Request.HandSize, new(Core.CalculationWorkPolicy.Unlimited));

        Core.ProbabilityCalculationResult result = CalculationRuntime.CompleteResult(prepared.Combos, prepared.Groups,
            M.CalculationModel.Fast, exact, budget, incomplete);

        Assert.Multiple(() => {
            Assert.That(result.Contract!.Accuracy, Is.EqualTo(Core.CalculationAccuracy.Uncertified));
            Assert.That(result.SearchAvailability!.Status, Is.EqualTo(Core.SearchAvailabilityStatus.Error));
            Assert.That(result.SearchAvailability.Detail, Does.Contain("InvalidOperationException"));
            Assert.That(result.ComboProbabilities, Has.Count.EqualTo(prepared.Combos.Count));
            Assert.That(result.GroupProbabilities, Has.Count.EqualTo(prepared.Groups.Count));
            Assert.That(result.SearchAvailability.Total, Is.Null);
        });
    }
    private static M.DrawActivationPriority? Priority(EngineCase fixture) => fixture.Options.ActivationOrder == ActivationOrder.Priority
        ? new(M.DrawActivationMode.CustomPriority, fixture.Options.PriorityEntryIds)
        : null;
}
