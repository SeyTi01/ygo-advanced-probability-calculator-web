using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Counting;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Certification;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.DrawResolution;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture, NonParallelizable]
public sealed class OrderedSearchCertificateTest {
    [TestCase(false, false), TestCase(false, true), TestCase(true, false), TestCase(true, true)]
    public void ForcedOrderedPoolsAndClassesBoundEveryLabeledPhysicalRow(bool defer, bool merge) {
        int deferred = 0;
        int strictImprovements = 0;
        foreach (SearchCertificateFixtures.Fixture fixture in SearchCertificateFixtures.OrderedCoverage()) {
            WorkBudget budget = new(CalculationWorkPolicy.Default);
            DrawActivationPriority? priority = fixture.Priority is null ? null : new(DrawActivationMode.CustomPriority, fixture.Priority);
            PostDrawCalculation execution = new(fixture.Deck, fixture.Hand, budget, priority, deferIdentities: defer);
            ExactCalculationResult upper = execution.CalculateSearch(fixture.Combos, fixture.Groups, false,
                new(fixture.Deck, budget), coalesceOrdinary: merge);
            ExactCalculationResult witness = execution.CalculateSearchLower(fixture.Combos, fixture.Groups, declineSearch: true);
            ExactCalculationResult elementary = execution.CalculateSearchLower(fixture.Combos, fixture.Groups, declineSearch: false, useKnownLoss: false);
            ExactCalculationResult lower = execution.CalculateSearchLower(fixture.Combos, fixture.Groups, declineSearch: false);
            SearchCertificateOracle.Counts oracle = SearchCertificateOracle.Enumerate(fixture.Deck, fixture.Hand, fixture.Combos, fixture.Groups, fixture.Priority);
            ExactProbability[] u = Rows(upper), l = Rows(lower), w = Rows(witness), old = Rows(elementary);
            for (int row = 0; row < u.Length; row++) {
                Assert.That(u[row].Numerator * oracle.Outcomes, Is.EqualTo(oracle.Wins[2][row] * u[row].Denominator), fixture.Name);
                Assert.That(w[row].Numerator * oracle.Outcomes, Is.EqualTo(oracle.Wins[4][row] * w[row].Denominator), fixture.Name);
                Assert.That(l[row].Numerator * w[row].Denominator, Is.GreaterThanOrEqualTo(w[row].Numerator * l[row].Denominator), fixture.Name);
                Assert.That(l[row].Numerator * oracle.Outcomes, Is.LessThanOrEqualTo(oracle.Wins[1][row] * l[row].Denominator), fixture.Name);
                Assert.That(old[row].Numerator * l[row].Denominator, Is.LessThanOrEqualTo(l[row].Numerator * old[row].Denominator), fixture.Name);
                Assert.That(old[row].Numerator * oracle.Outcomes, Is.LessThanOrEqualTo(oracle.Wins[3][row] * old[row].Denominator),
                    fixture.Name + ": the elementary bound covers arbitrary losses; known consumption excludes some of those allocations");
                if (l[row].Numerator * w[row].Denominator > w[row].Numerator * l[row].Denominator) { strictImprovements++; }
            }
            if (execution.Route == SolverRoute.SearchOrderedDeferred) { deferred++; }
            if (defer && merge) {
                TestContext.Out.WriteLine($"{fixture.Name}: combo 1 witness={w[1].ToFraction()}, class={l[1].ToFraction()}, upper={u[1].ToFraction()}");
            }
        }
        Assert.That(strictImprovements, Is.GreaterThan(0), "Actually exercises search survival beyond the empty assignment.");
        Assert.That(deferred, defer ? Is.GreaterThan(0) : Is.Zero);
    }

    [Test]
    public void FilteredDiscardIsInformativeForEachPositiveRowAndExactForImpossibleRows() {
        SearchCertificateFixtures.Fixture fixture = SearchCertificateFixtures.Fixtures().Single(value => value.Name == "minimal-filtered-target-discard");
        ProbabilityCalculationResult result = new ProbabilityCalculatorService().CalculateProbabilityResults(fixture.Deck, fixture.Combos, fixture.Hand, fixture.Groups, CalculationWorkPolicy.Default, new(SearchProjectionTestPolicy.DrawFirst(fixture.Deck, fixture.Priority), CertifySearchAvailability: true));
        SearchAvailabilityCertificate certificate = result.SearchAvailability!;
        Assert.That(result.Contract!.Accuracy, Is.EqualTo(CalculationAccuracy.Bounded));
        Assert.That(certificate.Total!.Lower, Is.EqualTo(new ProbabilityFraction("1", "2")));
        Assert.That(certificate.Total.Upper, Is.EqualTo(new ProbabilityFraction("17", "30")));
        Assert.That(certificate.Total.Informativeness, Is.EqualTo(SearchAvailabilityRowStatus.Informative));
        Assert.That(certificate.Combos[0].Lower, Is.EqualTo(certificate.Total.Lower));
        Assert.That(certificate.Groups[0].Lower, Is.EqualTo(certificate.Total.Lower));
        Assert.That(certificate.Combos[1].Informativeness, Is.EqualTo(SearchAvailabilityRowStatus.Exact));
        Assert.That(certificate.Groups[1].Informativeness, Is.EqualTo(SearchAvailabilityRowStatus.Exact));
    }

    [Test]
    public void UninformativeTotalStillPublishesExactZeroRowsWithoutGlobalPromotion() {
        CategoryBase target = new("Target");
        List<Card> cards = [new([], 1, id: "s", search: new(CardSelector.For(target), false)),
            new([target], 2, id: "t", drawCount: 1), new([], 1, id: "f")];
        List<Combo> combos = [new([new(target, 2, 2)], "Two targets", groupId: "g"),
            new([], "Impossible", cards: [new("absent", 1, 1)])];
        List<ComboGroup> groups = [new("g", "Targets"), new("empty", "Empty")];
        ProbabilityCalculationResult result = CalculationWire.ReadResult(CalculationWire.Execute(CalculationSnapshot.Capture(cards, combos, 2, groups, options: new(SearchProjectionTestPolicy.DrawFirst(cards))).Json));
        SearchCertificateOracle.Counts physical = SearchCertificateOracle.Enumerate(cards, 2, combos, groups, SearchProjectionTestPolicy.DrawFirst(cards).CardIds.ToArray());
        Assert.That(physical.Wins[1][0], Is.EqualTo(BigInteger.Zero));
        SearchAvailabilityCertificate certificate = result.SearchAvailability!;
        Assert.That(certificate.Status, Is.EqualTo(SearchAvailabilityStatus.Bounded));
        Assert.That(certificate.Total!.Informativeness, Is.EqualTo(SearchAvailabilityRowStatus.Uninformative));
        Assert.That(certificate.Combos[1].Informativeness, Is.EqualTo(SearchAvailabilityRowStatus.Exact));
        Assert.That(certificate.Groups[1].Informativeness, Is.EqualTo(SearchAvailabilityRowStatus.Exact));
        Assert.That(certificate.Detail, Does.Contain("uninformative"));
        Assert.That(result.Contract!.Accuracy, Is.EqualTo(CalculationAccuracy.Bounded));
    }

    [Test]
    public void WorkLimitedRefinementRetainsOnlyACompletedWitness() {
        SearchCertificateFixtures.Fixture fixture = SearchCertificateFixtures.Fixtures().Single(value => value.Name == "minimal-filtered-target-discard");
        // Locate a deterministic boundary after the cheap pass, then rerun with
        // a request-wide limit there. Never feed partial terminal sums to the UI.
        WorkBudget measure = new(CalculationWorkPolicy.Default);
        DrawEffectCalculation first = new(fixture.Deck, fixture.Hand, measure, SearchProjectionTestPolicy.DrawFirst(fixture.Deck, fixture.Priority));
        ExactCalculationResult upper = first.Calculate(fixture.Combos, fixture.Groups);
        first.DescribeCapability();
        long primaryWork = measure.Spent;
        ExactCalculationResult witness = first.SearchExecution!.CalculateLower(fixture.Combos, fixture.Groups, true);
        long witnessWork = measure.Spent - primaryWork;
        bool found = false;
        for (long allowance = primaryWork + witnessWork; allowance < primaryWork + witnessWork + 5000; allowance += 100) {
            WorkBudget budget = new(new(allowance));
            DrawEffectCalculation execution = new(fixture.Deck, fixture.Hand, budget, SearchProjectionTestPolicy.DrawFirst(fixture.Deck, fixture.Priority));
            ExactCalculationResult actual = execution.Calculate(fixture.Combos, fixture.Groups);
            SearchAvailabilityCertificate certificate = SearchAvailabilityCertification.TryCalculate(fixture.Combos, fixture.Groups, actual, budget, execution);
            if (certificate.Status == SearchAvailabilityStatus.Bounded && certificate.Detail.Contains("Limited refinement")
                && certificate.Total!.Lower.Numerator != "0") {
                Assert.That(certificate.Total.Lower, Is.EqualTo(witness.Total.ToFraction()));
                Assert.That(certificate.Total.Upper, Is.EqualTo(upper.Total.ToFraction()));
                found = true;
                break;
            }
        }
        Assert.That(found, Is.True, "The completed witness must survive exhaustion in the stronger pass.");
    }

    [Test]
    public void EveryFixtureHasACompletePerRowCertificate() {
        Dictionary<string, int> coverage = new();
        foreach (SearchCertificateFixtures.Fixture fixture in SearchCertificateFixtures.Fixtures()) {
            DrawActivationPriority priority = SearchProjectionTestPolicy.DrawFirst(fixture.Deck, fixture.Priority);
            ProbabilityCalculationResult result = new ProbabilityCalculatorService().CalculateProbabilityResults(fixture.Deck,
                fixture.Combos, fixture.Hand, fixture.Groups, CalculationWorkPolicy.Default, new(priority, true));
            SearchAvailabilityCertificate certificate = result.SearchAvailability!;
            if (result.Contract!.SolverRoute == "UnifiedActivation") {
                Assert.That(certificate.Status, Is.EqualTo(SearchAvailabilityStatus.Exact), fixture.Name);
                SearchProjectionTestPolicy.AssertCausalRows(result, fixture.Deck, fixture.Hand, fixture.Combos, fixture.Groups, priority);
            }
            Assert.That(certificate.Total, Is.Not.Null, fixture.Name + ": " + certificate.Detail);
            Assert.That(certificate.Combos.Length, Is.EqualTo(fixture.Combos.Count));
            Assert.That(certificate.Groups.Length, Is.EqualTo(fixture.Groups.Count));
            string key = $"{result.Contract!.SolverRoute}/{certificate.Status}/{certificate.Total!.Informativeness}";
            coverage[key] = coverage.GetValueOrDefault(key) + 1;
        }
        foreach ((string key, int count) in coverage.OrderBy(pair => pair.Key)) { TestContext.Out.WriteLine($"{key}: {count}"); }
    }

    private static ExactProbability[] Rows(ExactCalculationResult result) => [result.Total, .. result.Combos, .. result.Groups];
}
