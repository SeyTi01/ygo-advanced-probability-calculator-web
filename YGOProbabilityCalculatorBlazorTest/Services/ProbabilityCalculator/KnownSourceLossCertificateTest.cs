using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture, NonParallelizable]
public sealed class KnownSourceLossCertificateTest {
    internal static SessionState TwoTargetForty() {
        CategoryBase target = new("Archetype target");
        CategoryBase other = new("Other target");
        return new() {
            Cards = [new([], 3, id: "s", search: new(CardSelector.AnyOf([target.Identity, other.Identity]), false)),
                new([target], 3, id: "t"), new([other], 3, id: "b"), new([], 28, id: "f"),
                new([], 3, id: "d", drawCount: 2, drawOncePerTurn: true, postDrawDeckMillCount: 1)],
            HandSize = 5, Categories = [target, other],
            Combos = [new([new(target, 2, 2), new(other, 1, 5)], "Two targets and other", groupId: "g"),
                new([new(target, 1, 5), new(other, 2, 2)], "Target and two others", groupId: "g")],
            ComboGroups = [new("g", "Routes")]
        };
    }

    [Test]
    public void FortyCardMultiTargetSearchBecomesExactForEveryPublishedRow() {
        SessionState state = TwoTargetForty();
        ProbabilityCalculatorService service = new();
        ProbabilityCalculationResult projected = service.CalculateProbabilityResults(state.Cards, state.Combos, state.HandSize, state.ComboGroups,
            CalculationWorkPolicy.Default, new(CertifySearchAvailability: true));
        ProbabilityCalculationResult physical = service.CalculateProbabilityResults(state.Cards, state.Combos, state.HandSize, state.ComboGroups,
            CalculationWorkPolicy.Default, new(Mode: CalculationModel.Exact));
        SearchAvailabilityCertificate certificate = projected.SearchAvailability!;
        Assert.That(certificate.Status, Is.EqualTo(SearchAvailabilityStatus.Exact));
        AssertBounds(certificate, ApplicationResultFractions.From(physical), ApplicationResultFractions.From(projected));
    }

    [Test]
    public async Task FortyCardMillTightensEveryRowWithoutChangingPrimary() {
        SessionState state = await SearchAvailabilityCertificateTest.Example("mill2");
        ProbabilityCalculatorService service = new();
        ProbabilityCalculationResult projected = service.CalculateProbabilityResults(state.Cards, state.Combos, state.HandSize, state.ComboGroups,
            CalculationWorkPolicy.Default, new(CertifySearchAvailability: true));
        ProbabilityCalculationResult physical = service.CalculateProbabilityResults(state.Cards, state.Combos, state.HandSize, state.ComboGroups,
            CalculationWorkPolicy.Default, new(Mode: CalculationModel.Exact));
        SearchAvailabilityCertificate certificate = projected.SearchAvailability!;
        AssertBounds(certificate, ApplicationResultFractions.From(physical), ApplicationResultFractions.From(projected));
        Assert.That(certificate.Total!.Lower, Is.EqualTo(new ProbabilityFraction("33165161", "39151476")));
        Assert.That(certificate.Total.Upper, Is.EqualTo(new ProbabilityFraction("4819127", "5593068")));
    }

    [Test]
    public void MultipleTargetsStrictlyImproveAgainstIndependentPhysicalShuffleOracle() {
        SearchCertificateFixtures.Fixture fixture = SearchCertificateFixtures.Fixtures().Single(value => value.Name == "known-source-two-targets");
        ProbabilityCalculationResult projected = new ProbabilityCalculatorService().CalculateProbabilityResults(
            fixture.Deck, fixture.Combos, fixture.Hand, fixture.Groups, CalculationWorkPolicy.Default, new(CertifySearchAvailability: true));
        SearchCertificateOracle.Counts oracle = SearchCertificateOracle.Enumerate(fixture.Deck, fixture.Hand, fixture.Combos, fixture.Groups);
        ApplicationFraction[] values = oracle.Wins[1].Select(wins => new ApplicationFraction(wins, oracle.Outcomes)).ToArray();
        ApplicationResultFractions physical = new(values[0], values.Skip(1).Take(fixture.Combos.Count).ToArray(),
            values.Skip(1 + fixture.Combos.Count).ToArray(), null);
        AssertBounds(projected.SearchAvailability!, physical, ApplicationResultFractions.From(projected));
        ApplicationFraction refined = ApplicationFraction.From(projected.SearchAvailability!.Combos[0].Lower);
        Assert.That(refined.Numerator * oracle.Outcomes, Is.GreaterThan(oracle.Wins[0][1] * refined.Denominator));
    }

    private static void AssertBounds(SearchAvailabilityCertificate certificate, ApplicationResultFractions physical, ApplicationResultFractions primary) {
        SearchAvailabilityInterval[] intervals = [certificate.Total!, .. certificate.Combos, .. certificate.Groups];
        ApplicationFraction[] actual = [physical.Total, .. physical.Combos, .. physical.Groups];
        ApplicationFraction[] optimistic = [primary.Total, .. primary.Combos, .. primary.Groups];
        for (int row = 0; row < intervals.Length; row++) {
            ApplicationFraction lower = ApplicationFraction.From(intervals[row].Lower);
            ApplicationFraction upper = ApplicationFraction.From(intervals[row].Upper);
            Assert.That(lower.Numerator * actual[row].Denominator, Is.LessThanOrEqualTo(actual[row].Numerator * lower.Denominator));
            Assert.That(actual[row].Numerator * upper.Denominator, Is.LessThanOrEqualTo(upper.Numerator * actual[row].Denominator));
            Assert.That(upper.Numerator * optimistic[row].Denominator, Is.LessThanOrEqualTo(optimistic[row].Numerator * upper.Denominator));
        }
    }
}
