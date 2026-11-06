using CardProbability.EngineTests.Fixtures;
using CardProbability.Internal;
using CardProbability.Internal.Counting;
using CardProbability.Internal.DrawResolution;
using CardProbability.Internal.Resources;
using NUnit.Framework;
using Core = CardProbability.Internal.Contracts;

namespace CardProbability.EngineTests.Optimizations;

[TestFixture]
internal sealed class MixedDeferredStructureTest {
    [TestCase("pre-post"), TestCase("post-post"), TestCase("composite"), TestCase("two-costs")]
    [TestCase("hidden-pre"), TestCase("hidden-post"), TestCase("hidden-both"), TestCase("three-sources")]
    [TestCase("copies-opt"), TestCase("copies-non-opt"), TestCase("failure")]
    public void JointTraceAndPackedDeferredRepresentationsMatchEveryOracleRow(string shape) {
        EngineCase fixture = Fixture(shape);
        foreach (string[]? ids in new string[]?[] { null, ["remove", "draw", "cost"], ["draw"] }) {
            string[] priority = ids?.Where(id => fixture.Request.Entries.Any(entry => entry.Id == id)).ToArray() ?? [];
            EngineCase ordered = fixture with { Options = ids is null ? new() : new(activationOrder: ActivationOrder.Priority, priorityEntryIds: priority) };
            DrawSequenceOracle.Counts oracle = DrawSequenceOracle.Enumerate(ordered);
            Assert.That(oracle.Failed, Is.GreaterThan(System.Numerics.BigInteger.Zero));
            foreach (bool reverse in new[] { false, true }) {
                CalculationRequest request = new(reverse ? ordered.Request.Entries.Reverse() : ordered.Request.Entries,
                    ordered.Request.HandSize, ordered.Request.Goals, ordered.Request.Memberships, ordered.Request.Groups);
                EngineCase inputCase = ordered with { Request = request };
                PreparedCalculation input = inputCase.Prepare();
                foreach (bool defer in new[] { false, true }) {
                    WorkBudget budget = new(Core.CalculationWorkPolicy.Interactive);
                    PostDrawCalculation calculation = new(input.Cards, request.HandSize, budget, DeferredTestCases.Priority(ordered),
                        deferIdentities: defer, compactFrontierThreshold: 0);
                    DeferredTestCases.AssertRows(calculation.Calculate(input.Combos, input.Groups), oracle);
                    Assert.That(calculation.PackedLayers, Is.GreaterThan(0));
                    if (defer) {
                        Assert.That(calculation.DeferredRows, Is.GreaterThan(0));
                    }
                    PostDrawCalculation total = new(input.Cards, request.HandSize, new(Core.CalculationWorkPolicy.Interactive),
                        DeferredTestCases.Priority(ordered), deferIdentities: defer);
                    ExactProbability probability = total.Calculate(input.Combos, input.Groups, totalOnly: true).Total;
                    Assert.That(probability.Numerator * oracle.Total, Is.EqualTo(oracle.Wins[0] * probability.Denominator));
                }
            }
        }
    }

    [TestCase(1), TestCase(3), TestCase(5)]
    public void AtomicBatchExhaustionAndOpeningQueueOrderStayExact(int hand) =>
        DeferredTestCases.AssertDraw(Fixture("pre-post", hand));

    [TestCase("unfiltered"), TestCase("unfiltered-cost"), TestCase("first"), TestCase("lock")]
    public void UnprovedTimingAndHandSelectionRetainTheirExplicitFallback(string shape) {
        EngineCase fixture = Fixture("pre-post");
        DeckEntry[] entries = fixture.Request.Entries.ToArray();
        EffectPlan source = entries[1].Effect!;
        EffectPlan changed = shape switch {
            "unfiltered" => source with { RemovalEligibility = Eligibility.Any },
            "unfiltered-cost" => source with { HandCost = 1 },
            "first" => source with { FirstActivationOnly = true },
            _ => source with { BlocksLaterDraws = true }
        };
        entries[1] = entries[1] with { Effect = changed };
        EngineCase fallback = fixture with { Request = new(entries, 2, fixture.Request.Goals, fixture.Request.Memberships, fixture.Request.Groups) };
        DeferredTestCases.AssertDraw(fallback, deferred: false);
    }

    [Test]
    public void EligibilityForEitherSourceBreaksLabelSymmetryWithoutPoolingItsMaterial() {
        EngineCase fixture = Fixture("pre-post");
        PreparedCalculation input = fixture.Prepare();
        PostDrawCalculation calculation = new(input.Cards, 2, new(Core.CalculationWorkPolicy.Interactive));
        calculation.Calculate(input.Combos, input.Groups);
        Assert.That(calculation.DeferredRows, Is.EqualTo(3));
        DeferredTestCases.AssertDraw(fixture);
    }

    private static EngineCase Fixture(string shape, int hand = 2) {
        EffectPlan cost = new(DrawCount: 2, ActivationLimit: 1, HandCost: shape == "two-costs" ? 2 : 1,
            HandCostEligibility: new(shape == "composite" ? "composite-cost" : "cost"));
        EffectPlan removal = new(DrawCount: 2, RemovalCount: shape == "failure" ? 2 : 1,
            RemovalEligibility: new(shape == "composite" ? "composite-removal" : "removal"));
        if (shape == "post-post") {
            cost = cost with { HandCost = 0, HandCostEligibility = null, RemovalCount = 1, RemovalEligibility = new("cost") };
        }
        if (shape is "hidden-pre" or "hidden-both") {
            removal = removal with { HiddenDeckCost = 1 };
        }
        if (shape is "hidden-post" or "hidden-both") {
            cost = cost with { HiddenPostDrawMill = 1 };
        }
        if (shape == "copies-non-opt") {
            cost = cost with { ActivationLimit = null };
        }
        if (shape == "copies-opt") {
            removal = removal with { ActivationLimit = 1 };
        }
        List<DeckEntry> entries = [new("cost", shape.StartsWith("copies-", StringComparison.Ordinal) ? 2 : 1, cost),
            new("remove", 1, removal), new("both", 1), new("removal-only", 1), new("pool-ab", 1), new("pool-b", 1), new("pool-blank", 1)];
        if (shape == "three-sources") {
            entries.Add(new("draw", 1, new(DrawCount: 1)));
        }
        Membership[] memberships = [new("a", ["both", "pool-ab"]), new("b", ["both", "removal-only", "pool-ab", "pool-b"]),
            new("intersection", ["both", "pool-ab"]), new("cost", ["remove", "both"]), new("removal", ["cost", "both", "removal-only"]),
            new("composite-cost", ["both"]), new("composite-removal", ["both", "removal-only"])];
        Goal[] goals = [new("ab", [Requirement.InMembership("a", 1), Requirement.InMembership("b", 1)], groupId: "g"),
            new("same", [Requirement.InMembership("intersection", 1, 1)], groupId: "g"),
            new("direct", [Requirement.OfEntry("pool-b", 1, 1), Requirement.OfEntry("both", 1, 1)], groupId: "other"),
            new("source", [Requirement.OfEntry("cost", 1, 2)], groupId: "other"),
            new("exclude-cost", [Requirement.InMembership("cost", 0, 0)]), new("exclude-pool", [Requirement.OfEntry("pool-ab", 0, 0)]),
            new("exclude-blank", [Requirement.OfEntry("pool-blank", 0, 0)]),
            new("bounded", [Requirement.InMembership("b", 0, 1), Requirement.OfEntry("remove", 1, 1)]),
            new("alternative", [], [new([Requirement.InMembership("a", 1, 1), Requirement.OfEntry("pool-b", 1, 1)])])];
        return new(shape, new(entries, hand, goals, memberships, ["g", "other", "empty"]), new());
    }
}
