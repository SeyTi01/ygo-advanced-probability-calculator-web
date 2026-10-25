using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.Session;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Counting;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Certification;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;

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
    public void FortyCardMultiTargetSearchBecomesExactForEveryRow() {
        SessionState state = TwoTargetForty();
        WorkBudget budget = new(CalculationWorkPolicy.Default);
        DrawEffectCalculation execution = new(state.Cards, state.HandSize, budget, SearchProjectionTestPolicy.DrawFirst(state.Cards));
        ExactCalculationResult upper = execution.Calculate(state.Combos, state.ComboGroups);
        ExactCalculationResult old = execution.SearchExecution!.CalculateLower(state.Combos, state.ComboGroups, false);
        SearchAvailabilityCertificate certificate = SearchAvailabilityCertification.TryCalculate(state.Combos, state.ComboGroups, upper, budget, execution);
        ExactCalculationResult physical = new DrawEffectCalculation(state.Cards, state.HandSize,
            new(CalculationWorkPolicy.Default), SearchProjectionTestPolicy.DrawFirst(state.Cards), physicalSearch: true).Calculate(state.Combos, state.ComboGroups);
        AssertRows(old, certificate, physical, upper);
        Assert.That(certificate.Status, Is.EqualTo(SearchAvailabilityStatus.Exact));
        Assert.That(old.Total.Numerator * upper.Total.Denominator,
            Is.LessThan(upper.Total.Numerator * old.Total.Denominator));
    }

    [Test]
    public async Task FortyCardMillTightensEveryRowWithoutChangingPrimary() {
        SessionState state = await SearchAvailabilityCertificateTest.Example("mill2");
        WorkBudget budget = new(CalculationWorkPolicy.Default);
        DrawEffectCalculation execution = new(state.Cards, state.HandSize, budget, SearchProjectionTestPolicy.DrawFirst(state.Cards));
        ExactCalculationResult upper = execution.Calculate(state.Combos, state.ComboGroups);
        ExactCalculationResult old = execution.SearchExecution!.CalculateLower(state.Combos, state.ComboGroups, false);
        SearchAvailabilityCertificate certificate = SearchAvailabilityCertification.TryCalculate(state.Combos, state.ComboGroups, upper, budget, execution);
        ExactCalculationResult physical = new DrawEffectCalculation(state.Cards, state.HandSize,
            new(CalculationWorkPolicy.Default), SearchProjectionTestPolicy.DrawFirst(state.Cards), physicalSearch: true).Calculate(state.Combos, state.ComboGroups);
        AssertRows(old, certificate, physical, upper);
        Assert.That(old.Total.ToFraction(), Is.EqualTo(new ProbabilityFraction("727693", "860472")));
        Assert.That(certificate.Total!.Lower, Is.EqualTo(new ProbabilityFraction("33165161", "39151476")));
        Assert.That(certificate.Total.Upper, Is.EqualTo(new ProbabilityFraction("4819127", "5593068")));
    }

    [Test]
    public void MultipleTargetsStrictlyImproveAgainstIndependentPhysicalShuffleOracle() {
        SearchCertificateFixtures.Fixture fixture = SearchCertificateFixtures.Fixtures().Single(value => value.Name == "known-source-two-targets");
        WorkBudget budget = new(CalculationWorkPolicy.Default);
        DrawEffectCalculation execution = new(fixture.Deck, fixture.Hand, budget, SearchProjectionTestPolicy.DrawFirst(fixture.Deck, fixture.Priority));
        ExactCalculationResult upper = execution.Calculate(fixture.Combos, fixture.Groups);
        ExactCalculationResult old = execution.SearchExecution!.CalculateLower(fixture.Combos, fixture.Groups, false);
        SearchAvailabilityCertificate certificate = SearchAvailabilityCertification.TryCalculate(fixture.Combos, fixture.Groups, upper, budget, execution);
        SearchCertificateOracle.Counts oracle = SearchCertificateOracle.Enumerate(fixture.Deck, fixture.Hand, fixture.Combos, fixture.Groups,
            SearchProjectionTestPolicy.DrawFirst(fixture.Deck, fixture.Priority).CardIds.ToArray());
        ExactProbability[] values = oracle.Wins[1].Select(wins => new ExactProbability(wins, oracle.Outcomes)).ToArray();
        AssertRows(old, certificate, new(values[0], values.Skip(1).Take(fixture.Combos.Count).ToArray(),
            values.Skip(1 + fixture.Combos.Count).ToArray()), upper);
        SearchAvailabilityInterval row = certificate.Combos[0];
        Assert.That(BigInteger.Parse(row.Lower.Numerator) * old.Combos[0].Denominator,
            Is.GreaterThan(old.Combos[0].Numerator * BigInteger.Parse(row.Lower.Denominator)));
    }

    [Test]
    public async Task LimitAfterElementaryCompletionKeepsAllEarlierEndpoints() {
        SessionState state = await SearchAvailabilityCertificateTest.Example("mill2");
        WorkBudget measure = new(CalculationWorkPolicy.Default);
        DrawEffectCalculation first = new(state.Cards, state.HandSize, measure, SearchProjectionTestPolicy.DrawFirst(state.Cards));
        ExactCalculationResult upper = first.Calculate(state.Combos, state.ComboGroups);
        first.DescribeCapability();
        ExactCalculationResult old = first.SearchExecution!.CalculateLower(state.Combos, state.ComboGroups, false);
        // Enough for interval assembly and to begin the optional delta pass,
        // far short of its complete conditional search count.
        WorkBudget limited = new(new(measure.Spent + 10_000));
        DrawEffectCalculation execution = new(state.Cards, state.HandSize, limited, SearchProjectionTestPolicy.DrawFirst(state.Cards));
        ExactCalculationResult actual = execution.Calculate(state.Combos, state.ComboGroups);
        SearchAvailabilityCertificate certificate = SearchAvailabilityCertification.TryCalculate(state.Combos, state.ComboGroups, actual, limited, execution);
        Assert.That(certificate.Detail, Does.Contain("Limited refinement"));
        Assert.That(certificate.Status, Is.EqualTo(SearchAvailabilityStatus.Bounded));
        SearchAvailabilityInterval[] rows = [certificate.Total!, .. certificate.Combos, .. certificate.Groups];
        for (int row = 0; row < rows.Length; row++) {
            Assert.That(rows[row].Lower, Is.EqualTo(Rows(old)[row].ToFraction()));
            Assert.That(rows[row].Upper, Is.EqualTo(Rows(upper)[row].ToFraction()));
            Assert.That(Rows(actual)[row].ToFraction(), Is.EqualTo(Rows(upper)[row].ToFraction()));
        }
    }

    private static void AssertRows(
        ExactCalculationResult old,
        SearchAvailabilityCertificate certificate,
        ExactCalculationResult physical,
        ExactCalculationResult upper
    ) {
        SearchAvailabilityInterval[] rows = [certificate.Total!, .. certificate.Combos, .. certificate.Groups];
        for (int row = 0; row < rows.Length; row++) {
            ExactProbability lower = new(BigInteger.Parse(rows[row].Lower.Numerator), BigInteger.Parse(rows[row].Lower.Denominator));
            Assert.That(Rows(old)[row].Numerator * lower.Denominator,
                Is.LessThanOrEqualTo(lower.Numerator * Rows(old)[row].Denominator), $"old <= new, row {row}");
            Assert.That(lower.Numerator * Rows(physical)[row].Denominator,
                Is.LessThanOrEqualTo(Rows(physical)[row].Numerator * lower.Denominator), $"new <= physical, row {row}");
            Assert.That(Rows(physical)[row].Numerator * Rows(upper)[row].Denominator,
                Is.LessThanOrEqualTo(Rows(upper)[row].Numerator * Rows(physical)[row].Denominator), $"physical <= upper, row {row}");
            Assert.That(rows[row].Upper, Is.EqualTo(Rows(upper)[row].ToFraction()));
        }
    }

    private static ExactProbability[] Rows(ExactCalculationResult result) => [result.Total, .. result.Combos, .. result.Groups];
}
