using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture, NonParallelizable]
public sealed class OrderedThroughputTest {
    [Test]
    public void PublicRowsAndCertificatesRemainStableAcrossStorageTiers() {
        foreach (SearchCertificateFixtures.Fixture fixture in SearchCertificateFixtures.Fixtures()) {
            List<Card> deck = fixture.Deck.Where(card => card.Active && card.Copies > 0).ToList();
            DrawActivationPriority? priority = fixture.Priority is null ? null : new(DrawActivationMode.CustomPriority, fixture.Priority);
            ProbabilityCalculatorService service = new();
            ProbabilityCalculationResult standard = service.CalculateProbabilityResults(deck, fixture.Combos, fixture.Hand, fixture.Groups,
                CalculationWorkPolicy.Interactive, CalculationStoragePolicy.Standard, new(priority, true));
            ProbabilityCalculationResult high = service.CalculateProbabilityResults(deck, fixture.Combos, fixture.Hand, fixture.Groups,
                CalculationWorkPolicy.Interactive, CalculationStoragePolicy.High, new(priority, true));
            AssertRows(ApplicationResultFractions.From(standard), ApplicationResultFractions.From(high), fixture.Name);
            Assert.That(standard.SearchAvailability!.Total, Is.EqualTo(high.SearchAvailability!.Total));
            Assert.That(standard.SearchAvailability.Combos, Is.EqualTo(high.SearchAvailability.Combos));
            Assert.That(standard.SearchAvailability.Groups, Is.EqualTo(high.SearchAvailability.Groups));
        }
    }

    [Test]
    public void ElevenRoleStandardPrimaryAndCertificateMatchWideHighReference() {
        SessionState state = OrderedTerminalAggregationTest.Pressure(roles: 11);
        ProbabilityCalculatorService service = new();
        ProbabilityCalculationResult standard = service.CalculateProbabilityResults(state.Cards, state.Combos, state.HandSize, state.ComboGroups,
            CalculationWorkPolicy.Interactive, CalculationStoragePolicy.Standard, new(CertifySearchAvailability: true));
        ProbabilityCalculationResult reference = service.CalculateProbabilityResults(state.Cards, state.Combos, state.HandSize, state.ComboGroups,
            new(1000000000), CalculationStoragePolicy.High, new(CertifySearchAvailability: true));
        AssertRows(ApplicationResultFractions.From(standard), ApplicationResultFractions.From(reference), "eleven roles");
        Assert.That(standard.SearchAvailability!.Total, Is.EqualTo(reference.SearchAvailability!.Total));
        Assert.That(standard.SearchAvailability.Combos, Is.EqualTo(reference.SearchAvailability.Combos));
        Assert.That(standard.SearchAvailability.Groups, Is.EqualTo(reference.SearchAvailability.Groups));
    }




    private static void AssertRows(ApplicationResultFractions actual, ApplicationResultFractions expected, string label) {
        ApplicationFraction[] left = [actual.Total, .. actual.Combos, .. actual.Groups];
        ApplicationFraction[] right = [expected.Total, .. expected.Combos, .. expected.Groups];
        Assert.That(left.Length, Is.EqualTo(right.Length), label);
        for (int row = 0; row < left.Length; row++) {
            Assert.That(left[row].Numerator * right[row].Denominator,
                Is.EqualTo(right[row].Numerator * left[row].Denominator), label);
        }
    }
}
