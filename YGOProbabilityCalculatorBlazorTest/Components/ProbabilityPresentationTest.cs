using System.Globalization;
using System.Numerics;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazorTest.Components;

public sealed class ProbabilityPresentationTest {
    internal static CalculationResultContract Contract(CalculationAccuracy? accuracy, CalculationModel mode = CalculationModel.Fast) =>
        new(1, mode, mode, "Accepted model", mode == CalculationModel.OpeningHand ? CalculationRuleScope.OpeningHand : CalculationRuleScope.SavedEffectsFixedPolicy,
            CalculationCompletion.Success, accuracy, "Accepted reason");

    private static ProbabilityDisplay Bounded(string ln, string ld, string un, string ud, CultureInfo? culture = null) =>
        ProbabilityPresentation.Create(.99, Contract(CalculationAccuracy.Bounded),
            new(SearchAvailabilityStatus.Bounded, "Proof", new("Total", new(ln, ld), new(un, ud))), culture: culture ?? CultureInfo.InvariantCulture);

    [Test]
    public void RoundsTheRadiusFromPrintedCenterAndRetainsOriginalFractions() {
        ProbabilityDisplay display = Bounded("1", "2", "17", "30");
        Assert.That(display.Summary, Is.EqualTo("53.33% ±3.34%"));
        Assert.That(display.Detail, Does.Contain("[1/2, 17/30]").And.Contain("50.000000%").And.Contain("56.666667%"));
        Assert.That(Encloses("53.33%", "±3.33%", "1", "2", "17", "30"), Is.False);
        Assert.That(Encloses(display.Value, display.Accuracy, "1", "2", "17", "30"), Is.True);
    }

    [TestCase("1", "3", "2", "3")]
    [TestCase("0", "1", "1", "100000000000000000000000000000000000000000000000000")]
    [TestCase("99999999999999999999999999999999999999999999999999", "100000000000000000000000000000000000000000000000000", "1", "1")]
    [TestCase("1", "99999999999999999999999999999999999999999999999999", "2", "99999999999999999999999999999999999999999999999999")]
    [TestCase("0", "1", "1", "1")]
    public void ConservativeForLargeTinyNonDyadicAndBoundaryIntervals(string ln, string ld, string un, string ud) {
        ProbabilityDisplay display = Bounded(ln, ld, un, ud);
        Assert.That(Encloses(display.Value, display.Accuracy, ln, ld, un, ud), Is.True);
        Assert.That(display.Accuracy, Is.Not.EqualTo("±0%"));
        Assert.That(display.Summary, Does.Not.Contain("pp").And.Not.Contain("Uncertified"));
    }

    [Test]
    public void ManyRationalIntervalsEncloseBothExactEndpoints() {
        for (int denominator = 2; denominator <= 101; denominator++) {
            for (int numerator = 0; numerator < denominator; numerator++) {
                string ln = numerator.ToString(CultureInfo.InvariantCulture);
                string ld = denominator.ToString(CultureInfo.InvariantCulture);
                string un = (numerator + 1).ToString(CultureInfo.InvariantCulture);
                ProbabilityDisplay display = Bounded(ln, ld, un, ld);
                Assert.That(Encloses(display.Value, display.Accuracy, ln, ld, un, ld), Is.True);
            }
        }
    }

    [TestCase("0", "1", "0", "2", "0.00%")]
    [TestCase("1", "3", "2", "6", "33.33%")]
    public void RationalEqualityIsExactPerRowEvenWhenGlobalResultIsBounded(string ln, string ld, string un, string ud, string expected) {
        ProbabilityDisplay display = Bounded(ln, ld, un, ud);
        Assert.That(display.Value, Is.EqualTo(expected));
        Assert.That(display.Accuracy, Is.EqualTo("±0%"));
        Assert.That(display.Uninformative, Is.False);
    }

    [Test]
    public void ZeroLowerEndpointIsBoundedAndCultureUsesExplicitSeparator() {
        Assert.That(Bounded("0", "1", "1", "2").Summary, Is.EqualTo("25.00% ±25.00% · Uninformative"));
        ProbabilityDisplay german = Bounded("1", "2", "17", "30", CultureInfo.GetCultureInfo("de-DE"));
        Assert.That(german.Summary, Is.EqualTo("53,33% ±3,34%"));
        Assert.That(german.Detail, Does.Contain("50,000000%"));
    }

    [TestCase(CalculationModel.OpeningHand)]
    [TestCase(CalculationModel.Fast)]
    [TestCase(CalculationModel.Exact)]
    public void ActualExactAccuracyWinsOverRequestedModeWithoutAnyCertificate(CalculationModel mode) {
        ProbabilityDisplay display = ProbabilityPresentation.Create(.5, Contract(CalculationAccuracy.Exact, mode), null);
        Assert.That(display.Accuracy, Is.EqualTo("±0%"));
    }

    [Test]
    public void ExactCertificateDisplaysItsEndpointEvenBelowOptimisticPrimary() {
        CalculationResultContract contract = Contract(CalculationAccuracy.Exact) with {
            Fractions = new(new("3", "4"), [], [])
        };
        ProbabilityDisplay display = ProbabilityPresentation.Create(.75, contract,
            new(SearchAvailabilityStatus.Exact, "Proof", new("Total", new("1", "2"), new("1", "2"))),
            culture: CultureInfo.InvariantCulture);
        Assert.That(display.Summary, Is.EqualTo("50.00% ±0%"));
        Assert.That(display.Detail, Does.Contain("Modeled primary fraction: 3/4"));
        Assert.That(display.Evidence, Does.Contain("modeled optimistic primary: 3/4"));
    }

    [TestCase(SearchAvailabilityStatus.NotCertified)]
    [TestCase(SearchAvailabilityStatus.Limited)]
    [TestCase(SearchAvailabilityStatus.Error)]
    public void UnavailableIntervalsNeverInventMargins(SearchAvailabilityStatus status) {
        ProbabilityDisplay display = ProbabilityPresentation.Create(.5, Contract(CalculationAccuracy.Uncertified), new(status, "Specific reason"));
        Assert.That(display.Accuracy, Is.EqualTo("⚠ Uncertified"));
        Assert.That(display.Detail, Does.Contain("Specific reason"));
        Assert.That(ProbabilityPresentation.Create(.5, null, null).Accuracy, Is.EqualTo("⚠ Uncertified"));
    }

    [TestCase("2", "1", "1", "1")]
    [TestCase("1", "2", "1", "3")]
    [TestCase("1", "0", "1", "1")]
    [TestCase("bad", "2", "1", "1")]
    public void InvalidIntervalsDegradeConservatively(string ln, string ld, string un, string ud) =>
        Assert.That(Bounded(ln, ld, un, ud).Accuracy, Is.EqualTo("⚠ Uncertified"));

    private static bool Encloses(string value, string accuracy, string ln, string ld, string un, string ud) {
        BigInteger center = new(decimal.Parse(value.TrimEnd('%'), CultureInfo.InvariantCulture) * 100m);
        BigInteger radius = new(decimal.Parse(accuracy.TrimStart('±').TrimEnd('%'), CultureInfo.InvariantCulture) * 100m);

        return (center - radius) * BigInteger.Parse(ld) <= BigInteger.Parse(ln) * 10000
            && (center + radius) * BigInteger.Parse(ud) >= BigInteger.Parse(un) * 10000;
    }
}
