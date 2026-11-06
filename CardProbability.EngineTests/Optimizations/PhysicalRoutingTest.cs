using CardProbability.EngineTests.Fixtures;
using CardProbability.Internal;
using CardProbability.Internal.DrawResolution;
using CardProbability.Internal.Certification;
using CardProbability.Internal.Resources;
using Core = CardProbability.Internal.Contracts;

namespace CardProbability.EngineTests.Optimizations;

[TestFixture, NonParallelizable]
public sealed class PhysicalRoutingTest {
    [TestCase(false, 2), TestCase(true, 4)]
    public void UnobservedOrdinaryLabelsAndUnremovableSearchersRetainTheirDeferredPartition(bool separateMaterial, int expectedRows) {
        DeckEntry[] entries = separateMaterial
            ? [new("source", 1, new(Search: new(new("a")))), new("target", 1), new("material", 1),
                new("draw", 1, new(DrawCount: 1, HandCost: 1, HandCostEligibility: new("b"))),
                new("ordinary-a", 1), new("ordinary-blank", 1)]
            : [new("source", 1, new(Search: new(new("a")))), new("target", 1),
                new("draw", 1, new(DrawCount: 1, HandCost: 1, HandCostEligibility: new("b"))),
                new("second-target", 1), new("blank", 2)];
        Goal[] goals = [new("a", [Requirement.InMembership("a", 1, 2)], groupId: "g1"),
            new("source", [Requirement.InMembership("spell", 1, 2)], groupId: "g2"),
            .. (separateMaterial ? new[] { new Goal("direct", [Requirement.OfEntry("target", 1, 2)], groupId: "g1") } : Array.Empty<Goal>())];
        EngineCase fixture = new("deferred-partition", new(entries, 2, goals,
            [new("a", separateMaterial ? ["target", "ordinary-a"] : ["target", "second-target"]),
                new("b", separateMaterial ? ["material"] : ["target", "second-target"]), new("spell", ["source"])], ["g1", "g2"]), new());
        PreparedCalculation input = fixture.Prepare();
        WorkBudget budget = new(Core.CalculationWorkPolicy.Interactive);
        PostDrawCalculation calculation = new(input.Cards, 2, budget);
        ExactCalculationResult result = calculation.CalculateSearch(input.Combos, input.Groups, false, new(input.Cards, budget));
        Assert.That(calculation.DeferredRows, Is.EqualTo(expectedRows));
        SearchCertificateOracle.Counts oracle = SearchCertificateOracle.Enumerate(fixture);
        PhysicalEnumerationTest.AssertRows(result, oracle.Wins[2], oracle.Outcomes, "deferred-partition/" + expectedRows);
    }

    [Test]
    public void ExhaustiveExistingFixturesRetainPhysicalCountStateCoverage() {
        int countState = 0;
        foreach (EngineCase fixture in SearchCertificateFixtures.Fixtures()) {
            PreparedCalculation prepared = fixture.Prepare();
            WorkBudget budget = new(Core.CalculationWorkPolicy.Default);
            DrawEffectCalculation calculation = new(prepared.Cards, fixture.Request.HandSize, budget, PhysicalEnumerationTest.Priority(fixture), physicalSearch: true);
            ExactCalculationResult result = calculation.Calculate(prepared.Combos, prepared.Groups);
            SearchCertificateOracle.Counts oracle = SearchCertificateOracle.Enumerate(fixture);
            PhysicalEnumerationTest.AssertRows(result, oracle.Wins[1], oracle.Outcomes, fixture.Name);
            if (calculation.DescribeCapability().Route == SolverRoute.SearchPhysicalCountState) { countState++; }
        }
        Assert.That(countState, Is.GreaterThan(10));
    }

    [TestCase(false, false), TestCase(false, true), TestCase(true, false), TestCase(true, true)]
    public void JointDepletionWithConsumedSourcesAndIndependentEntryLimits(bool opt, bool split) {
        int? allowance = opt ? 1 : null;
        List<DeckEntry> entries = [new("s", 1, new(ActivationLimit: allowance, Search: new(Eligibility.Any))),
            new("z", 1, new(ActivationLimit: allowance, Search: new(new("a")))), new("t", 2),
            new("d", split ? 1 : 2, new(DrawCount: 1, ActivationLimit: allowance, HiddenDeckCost: 1)),
            new("e", 1, new(DrawCount: 1, ActivationLimit: allowance)), new("f", 1)];
        if (split) { entries.Add(new("other-d", 1, new(DrawCount: 1, ActivationLimit: allowance, HiddenPostDrawMill: 1))); }
        string[] a = split ? ["z", "t", "d", "other-d"] : ["z", "t", "d"];
        string[] priority = split ? ["e", "other-d", "d"] : ["e", "d"];
        EngineCase fixture = new("joint", new(entries, 2, [
            new("distinct", [Requirement.InMembership("a", 1, 3), Requirement.InMembership("b", 1, 3)], groupId: "g"),
            new("zero", [Requirement.InMembership("a", 0, 0)], groupId: "g"),
            new("maximum", [Requirement.InMembership("b", 0, 1)], groupId: "g"),
            new("forbidden", [Requirement.OfEntry("s", 0, 0), Requirement.OfEntry("d", 1, 3)], groupId: "h"),
            new("same", [Requirement.InMembership("both", 1, 1)], groupId: "h"),
            new("alternative", [], [new([Requirement.OfEntry("d", 1, 3), Requirement.InMembership("b", 2, 3)])], "h")
        ], [new("a", a), new("b", ["s", "t", "e"]), new("both", ["t"])], ["g", "h"]),
            new(activationOrder: ActivationOrder.Priority, priorityEntryIds: priority));
        PreparedCalculation prepared = fixture.Prepare();
        DrawEffectCalculation calculation = new(prepared.Cards, 2, new(Core.CalculationWorkPolicy.Default), PhysicalEnumerationTest.Priority(fixture), physicalSearch: true);
        ExactCalculationResult result = calculation.Calculate(prepared.Combos, prepared.Groups);
        SearchCertificateOracle.Counts oracle = SearchCertificateOracle.Enumerate(fixture);
        PhysicalEnumerationTest.AssertRows(result, oracle.Wins[1], oracle.Outcomes, "joint");
        Assert.That(calculation.DescribeCapability().Route, Is.EqualTo(SolverRoute.SearchPhysicalCountState));
    }

    [TestCase(false, false), TestCase(false, true), TestCase(true, false), TestCase(true, true)]
    public void HiddenLossWithOrderedLocksCostsAndIndependentEntryOpt(bool first, bool custom) {
        EngineCase fixture = new("ordered-locks", new([
            new("s", 1, new(Search: new(Eligibility.Any))), new("z", 1, new(Search: new(new("target")))),
            new("aa", 2), new("zz", 1),
            new("d", 1, new(DrawCount: 1, FirstActivationOnly: first, BlocksLaterDraws: !first,
                RemovalCount: 1, RemovalEligibility: new("target"), HiddenDeckCost: 1)),
            new("e", 1, new(DrawCount: 1, HandCost: 1, HandCostEligibility: new("target"), HiddenPostDrawMill: 1))
        ], 3, [new("one-two", [Requirement.InMembership("target", 1, 2)], groupId: "g"),
            new("none", [Requirement.InMembership("target", 0, 0)], groupId: "g"), new("no-draw", [Requirement.OfEntry("e", 0, 0)])],
            [new("target", ["aa", "zz", "e"])], ["g", "empty"]),
            custom ? new(activationOrder: ActivationOrder.Priority, priorityEntryIds: ["e", "d"]) : new());
        PreparedCalculation prepared = fixture.Prepare();
        DrawEffectCalculation calculation = new(prepared.Cards, 3, new(Core.CalculationWorkPolicy.Default), PhysicalEnumerationTest.Priority(fixture), physicalSearch: true);
        ExactCalculationResult result = calculation.Calculate(prepared.Combos, prepared.Groups);
        SearchCertificateOracle.Counts oracle = SearchCertificateOracle.Enumerate(fixture);
        PhysicalEnumerationTest.AssertRows(result, oracle.Wins[1], oracle.Outcomes, "ordered-locks");
        Assert.That(calculation.DescribeCapability().Route, Is.EqualTo(SolverRoute.SearchPhysicalFrontier));
    }

    [Test]
    public void ManyObservationallyEqualEntriesCompleteWithinOriginalLimits() {
        DeckEntry[] entries = [.. Enumerable.Range(0, 38).Select(index => new DeckEntry("t" + index, 1)),
            new("s", 1, new(Search: new(new("target")))), new("d", 1, new(DrawCount: 1, RemovalCount: 1))];
        EngineCase fixture = new("equal-entries", new(entries, 5, [new("target", [Requirement.InMembership("target", 1, 6)])],
            [new("target", entries.Take(38).Select(entry => entry.Id))]), new());
        PreparedCalculation prepared = fixture.Prepare();
        WorkBudget budget = new(Core.CalculationWorkPolicy.Default);
        ExactCalculationResult result = new DrawEffectCalculation(prepared.Cards, 5, budget, physicalSearch: true).Calculate(prepared.Combos, []);
        Assert.That(result.Total.Numerator, Is.EqualTo(result.Total.Denominator));
        Assert.That(result.Combos.Single(), Is.EqualTo(result.Total));
        Assert.That(budget.Spent, Is.LessThan(Core.CalculationWorkPolicy.Default.WorkUnits));
        Assert.That(budget.PeakCells, Is.LessThanOrEqualTo(Core.CalculationStoragePolicy.Standard.CellLimit));
    }
}
