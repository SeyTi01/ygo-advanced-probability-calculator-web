using System.Numerics;
using CardProbability.EngineTests.Fixtures;
using CardProbability.Internal;
using CardProbability.Internal.Certification;
using CardProbability.Internal.Counting;
using CardProbability.Internal.Resources;
using CardProbability.Internal.Search;
using Core = CardProbability.Internal.Contracts;

namespace CardProbability.EngineTests.Optimizations;

[TestFixture]
internal sealed class KnownLossRefinementRegressionTest {
    [Test]
    public void FortyCardTwoTargetRefinementIsExactForEveryPrimaryRow() {
        EngineCase fixture = new("two-target-forty", new([
            new("s", 3, new(Search: new(new("either")))), new("t", 3), new("b", 3), new("f", 28),
            new("d", 3, new(DrawCount: 2, ActivationLimit: 1, HiddenPostDrawMill: 1))
        ], 5, [
            new("two-target", [Requirement.InMembership("target", 2, 2), Requirement.InMembership("other", 1, 5)], groupId: "g"),
            new("two-other", [Requirement.InMembership("target", 1, 5), Requirement.InMembership("other", 2, 2)], groupId: "g")
        ], [new("target", ["t"]), new("other", ["b"]), new("either", ["t", "b"])], ["g"]), new());
        PreparedCalculation input = fixture.Prepare();
        WorkBudget budget = new(Core.CalculationWorkPolicy.Default);
        DrawEffectCalculation execution = new(input.Cards, 5, budget);
        ExactCalculationResult upper = execution.Calculate(input.Combos, input.Groups);
        ExactCalculationResult elementary = execution.SearchExecution!.CalculateLower(input.Combos, input.Groups, false);
        Core.SearchAvailabilityCertificate certificate = SearchAvailabilityCertification.TryCalculate(input.Combos, input.Groups, upper, budget, execution);
        ExactCalculationResult physical = new DrawEffectCalculation(input.Cards, 5, new(Core.CalculationWorkPolicy.Default), physicalSearch: true)
            .Calculate(input.Combos, input.Groups);
        Assert.That(certificate.Status, Is.EqualTo(Core.SearchAvailabilityStatus.Exact));
        Core.SearchAvailabilityInterval[] intervals = [certificate.Total!, .. certificate.Combos, .. certificate.Groups];
        for (int row = 0; row < intervals.Length; row++) {
            ExactProbability lower = new(BigInteger.Parse(intervals[row].Lower.Numerator), BigInteger.Parse(intervals[row].Lower.Denominator));
            ExactProbability old = elementary.ToRows()[row];
            ExactProbability actual = physical.ToRows()[row];
            ExactProbability optimistic = upper.ToRows()[row];
            Assert.That(old.Numerator * lower.Denominator, Is.LessThanOrEqualTo(lower.Numerator * old.Denominator));
            Assert.That(lower, Is.EqualTo(actual));
            Assert.That(actual.Numerator * optimistic.Denominator, Is.LessThanOrEqualTo(optimistic.Numerator * actual.Denominator));
        }
        Assert.That(elementary.Total.Numerator * upper.Total.Denominator, Is.LessThan(upper.Total.Numerator * elementary.Total.Denominator));
    }
    [Test]
    public void OrderedLossTableTakesWorstCompatibleHistoryAndHonorsLimits() {
        EngineCase fixture = new("loss-table", new([
            new("known", 1, new(DrawCount: 1, HandCost: 1, HandCostEligibility: new("material"))),
            new("unknown", 1, new(DrawCount: 1, HiddenPostDrawMill: 1)),
            new("payment", 1), new("target", 2), new("search", 1, new(Search: new(new("target"))))
        ], 0, [], [new("material", ["payment"]), new("target", ["target"])]), new());
        PreparedCalculation input = fixture.Prepare();
        int[] loss = OrderedSearchLossBudget.Create(input.Cards, new(Core.CalculationWorkPolicy.Default));
        Assert.That(loss[2], Is.EqualTo(1));
        Assert.That(loss[4], Is.EqualTo(1));
        Assert.That(loss[1], Is.EqualTo(1));
        Assert.Throws<Core.ProbabilityCalculationLimitException>(() => OrderedSearchLossBudget.Create(input.Cards, new(new(1))));
        EngineCase oversized = new("oversized", new([new("large", 40000)], 0, []), new());
        Core.ProbabilityCalculationLimitException error = Assert.Throws<Core.ProbabilityCalculationLimitException>(() =>
            OrderedSearchLossBudget.Create(oversized.Prepare().Cards, new(Core.CalculationWorkPolicy.Default)))!;
        Assert.That(error.Reason, Is.EqualTo(Core.ProbabilityCalculationLimitReason.Storage));
    }}
