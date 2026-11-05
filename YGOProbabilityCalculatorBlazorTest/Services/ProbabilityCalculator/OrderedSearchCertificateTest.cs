using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture, NonParallelizable]
public sealed class OrderedSearchCertificateTest {
    [Test]
    public void OrderedPublishedBoundsCoverEveryLabeledPhysicalRow() {
        int strictImprovements = 0;
        foreach (SearchCertificateFixtures.Fixture fixture in SearchCertificateFixtures.OrderedCoverage()) {
            DrawActivationPriority? priority = fixture.Priority is null ? null : new(DrawActivationMode.CustomPriority, fixture.Priority);
            ProbabilityCalculationResult result = new ProbabilityCalculatorService().CalculateProbabilityResults(
                fixture.Deck, fixture.Combos, fixture.Hand, fixture.Groups, CalculationWorkPolicy.Default, new(priority, true));
            ApplicationResultFractions fractions = ApplicationResultFractions.From(result);
            ApplicationFraction[] primary = [fractions.Total, .. fractions.Combos, .. fractions.Groups];
            SearchAvailabilityCertificate certificate = result.SearchAvailability!;
            SearchAvailabilityInterval[] bounds = [certificate.Total!, .. certificate.Combos, .. certificate.Groups];
            SearchCertificateOracle.Counts oracle = SearchCertificateOracle.Enumerate(fixture.Deck, fixture.Hand, fixture.Combos, fixture.Groups, fixture.Priority);
            for (int row = 0; row < primary.Length; row++) {
                ApplicationFraction lower = ApplicationFraction.From(bounds[row].Lower);
                ApplicationFraction upper = ApplicationFraction.From(bounds[row].Upper);
                Assert.That(primary[row].Numerator * oracle.Outcomes, Is.EqualTo(oracle.Wins[2][row] * primary[row].Denominator), fixture.Name);
                Assert.That(lower.Numerator * oracle.Outcomes, Is.GreaterThanOrEqualTo(oracle.Wins[4][row] * lower.Denominator), fixture.Name);
                Assert.That(lower.Numerator * oracle.Outcomes, Is.GreaterThanOrEqualTo(oracle.Wins[0][row] * lower.Denominator), fixture.Name);
                Assert.That(lower.Numerator * oracle.Outcomes, Is.LessThanOrEqualTo(oracle.Wins[1][row] * lower.Denominator), fixture.Name);
                Assert.That(upper.Numerator * oracle.Outcomes, Is.GreaterThanOrEqualTo(oracle.Wins[1][row] * upper.Denominator), fixture.Name);
                if (lower.Numerator * oracle.Outcomes > oracle.Wins[4][row] * lower.Denominator) {
                    strictImprovements++;
                }
            }
        }
        Assert.That(strictImprovements, Is.GreaterThan(0));
    }

    [Test]
    public void FilteredDiscardIsInformativeForEachPositiveRowAndExactForImpossibleRows() {
        SearchCertificateFixtures.Fixture fixture = SearchCertificateFixtures.Fixtures().Single(value => value.Name == "minimal-filtered-target-discard");
        ProbabilityCalculationResult result = new ProbabilityCalculatorService().CalculateProbabilityResults(fixture.Deck, fixture.Combos, fixture.Hand, fixture.Groups, CalculationWorkPolicy.Default, new(CertifySearchAvailability: true));
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
        ProbabilityCalculationResult result = CalculationWire.ReadResult(CalculationWire.Execute(CalculationSnapshot.Capture(cards, combos, 2, groups).Json));
        SearchCertificateOracle.Counts physical = SearchCertificateOracle.Enumerate(cards, 2, combos, groups);
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
        ProbabilityCalculatorService service = new();
        ProbabilityCalculationResult reference = service.CalculateProbabilityResults(fixture.Deck, fixture.Combos, fixture.Hand, fixture.Groups,
            CalculationWorkPolicy.Default, new(CertifySearchAvailability: true));
        SearchCertificateOracle.Counts oracle = SearchCertificateOracle.Enumerate(fixture.Deck, fixture.Hand, fixture.Combos, fixture.Groups);
        ApplicationFraction witness = new(oracle.Wins[4][0], oracle.Outcomes);
        bool found = false;
        for (long allowance = 100; allowance <= 50000; allowance += 100) {
            ProbabilityCalculationResult result;
            try {
                result = service.CalculateProbabilityResults(fixture.Deck, fixture.Combos, fixture.Hand, fixture.Groups,
                    new(allowance), new(CertifySearchAvailability: true));
            }
            catch (ProbabilityCalculationLimitException) {
                continue;
            }
            SearchAvailabilityCertificate? certificate = result.SearchAvailability;
            if (certificate?.Status == SearchAvailabilityStatus.Bounded && certificate.Detail.Contains("Limited refinement")
                && certificate.Total!.Lower.Numerator != "0") {
                Assert.That(certificate.Total.Lower, Is.EqualTo(witness.ToFraction()));
                Assert.That(certificate.Total.Upper, Is.EqualTo(reference.Contract!.Fractions!.Total));
                Assert.That(result.Contract!.Fractions!.Total, Is.EqualTo(reference.Contract.Fractions.Total));
                Assert.That(result.Contract.Fractions.Combos, Is.EqualTo(reference.Contract.Fractions.Combos));
                Assert.That(result.Contract.Fractions.Groups, Is.EqualTo(reference.Contract.Fractions.Groups));
                found = true;
                break;
            }
        }
        Assert.That(found, Is.True);
    }

    [Test]
    public void EveryFixtureHasACompletePerRowCertificate() {
        Dictionary<string, int> coverage = new();
        foreach (SearchCertificateFixtures.Fixture fixture in SearchCertificateFixtures.Fixtures()) {
            DrawActivationPriority? priority = fixture.Priority is null ? null : new(DrawActivationMode.CustomPriority, fixture.Priority);
            ProbabilityCalculationResult result = new ProbabilityCalculatorService().CalculateProbabilityResults(fixture.Deck,
                fixture.Combos, fixture.Hand, fixture.Groups, CalculationWorkPolicy.Default, new(priority, true));
            SearchAvailabilityCertificate certificate = result.SearchAvailability!;
            Assert.That(certificate.Total, Is.Not.Null, fixture.Name + ": " + certificate.Detail);
            Assert.That(certificate.Combos.Length, Is.EqualTo(fixture.Combos.Count));
            Assert.That(certificate.Groups.Length, Is.EqualTo(fixture.Groups.Count));
            string key = $"{result.Contract!.Accuracy}/{certificate.Status}/{certificate.Total!.Informativeness}";
            coverage[key] = coverage.GetValueOrDefault(key) + 1;
        }
        foreach ((string key, int count) in coverage.OrderBy(pair => pair.Key)) { TestContext.Out.WriteLine($"{key}: {count}"); }
    }

}
