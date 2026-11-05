using System.Numerics;
using CardProbability.EngineTests.Fixtures;
using static CardProbability.EngineTests.Optimizations.DeferredTestCases;

namespace CardProbability.EngineTests.Optimizations;

[TestFixture]
public sealed class PostDrawDeferredTest {
    private static EngineCase Case(int sources, int hand, bool opt = false, bool sourceEligible = false) => Create(
        "filtered post-removal", [new("dark-ab", 1), new("dark-b", 1), new("light-ab", 1), new("light-b", 1),
            Draw("source", sources, limit: opt ? 1 : null, removal: 1, removalSet: "dark")], hand,
        [new("distinct", [Member("a"), Member("b")], groupId: "g"),
            new("direct", [Entry("dark-b", 1, 1)], groupId: "g"), Goal("dark-exclusion", Member("dark", 0, 0)),
            Goal("direct-exclusion", Entry("light-b", 0, 0)), Goal("intersection", Member("ab", 1, 1)),
            new("alternative", [], [new([Member("a", 1, 1), Entry("source", 1, 2)])]),
            Goal("two-direct", Entry("light-ab", 1, 1), Entry("light-b", 1, 1))],
        [new("dark", sourceEligible ? ["dark-ab", "dark-b", "source"] : ["dark-ab", "dark-b"]),
            new("a", ["dark-ab", "light-ab"]), new("b", ["dark-ab", "dark-b", "light-ab", "light-b"]),
            new("ab", ["dark-ab", "light-ab"])], ["g", "empty"]);

    private static IEnumerable<TestCaseData> Cases() {
        foreach (int sources in new[] { 1, 2, 3 }) {
            foreach (int hand in new[] { 0, 1, 2, 4 }) {
                foreach (bool opt in new[] { false, true }) {
                    yield return new(sources, hand, opt);
                }
            }
        }
    }

    [TestCaseSource(nameof(Cases))]
    public void DeferredSubsetsMatchIndependentPhysicalPermutations(int sources, int hand, bool opt) => AssertDraw(Case(sources, hand, opt), true);

    [TestCase(false), TestCase(true)]
    public void SourceMayBeRemovalMaterialWithoutLosingOptOrQueueIdentity(bool opt) => AssertDraw(Case(3, 2, opt, true), true);

    [Test]
    public void FailureMassIsPreservedAndNoSourceOpeningStillSucceeds() {
        EngineCase original = Case(2, 2);
        EngineCase fixture = Change(original, entries: original.Request.Entries.Where(entry => !entry.Id.StartsWith("dark", StringComparison.Ordinal)),
            goals: [Goal("light-ab", Entry("light-ab", 1, 1)), Goal("light-b", Entry("light-b", 1, 1))],
            memberships: [new("dark", []), new("a", ["light-ab"]), new("b", ["light-ab", "light-b"]), new("ab", ["light-ab"])]);
        DrawSequenceOracle.Counts oracle = AssertDraw(fixture);
        Assert.That(oracle.Failed, Is.GreaterThan(BigInteger.Zero));
        Assert.That(oracle.Wins[0], Is.GreaterThan(BigInteger.Zero).And.LessThan(oracle.Total));
    }

    [Test]
    public void DistinctSameNameEntriesStaySeparateAndUnsafeShapesRetainOrderedResolution() {
        EngineCase original = Case(1, 2);
        DeckEntry source = original.Request.Entries[^1];
        foreach (EffectPlan effect in new[] { source.Effect! with { HandCost = 1 }, source.Effect! with { FirstActivationOnly = true } }) {
            AssertDraw(Change(original, entries: [.. original.Request.Entries[..^1], source with { Effect = effect }]), false);
        }
        EngineCase split = Change(original, entries: [.. original.Request.Entries,
            Draw("source-two", limit: 1, removal: 1, removalSet: "dark")], priority: ["source-two", "source"]);
        AssertDraw(split, true);
    }

    [TestCase(1, 0), TestCase(0, 1), TestCase(1, 1)]
    public void HiddenOperationsPreserveFilteredPostRemovalExchangeability(int hiddenCost, int mill) {
        EngineCase fixture = Case(2, 2);
        AssertDraw(Change(fixture, entries: fixture.Request.Entries.Select(entry => entry.Effect is null ? entry
            : entry with { Effect = entry.Effect with { HiddenDeckCost = hiddenCost, HiddenPostDrawMill = mill } })), true);
    }

    [Test]
    public void MandatoryTwoCardRemovalAndCompositeSelectorRemainExact() {
        EngineCase fixture = Case(2, 2);
        AssertDraw(Change(fixture, entries: fixture.Request.Entries.Select(entry => entry.Effect is null ? entry
            : entry with { Effect = entry.Effect with { RemovalCount = 2, RemovalEligibility = new("dark-and-b") } }),
            memberships: [.. fixture.Request.Memberships, new("dark-and-b", ["dark-ab", "dark-b"])]), true);
    }

    [Test]
    public void RemovalUsesAllActiveComboRelevanceForEveryOutput() {
        EngineCase fixture = Case(2, 3);
        AssertDraw(Change(fixture, entries: [new("irrelevant-dark", 1), .. fixture.Request.Entries],
            memberships: fixture.Request.Memberships.Select(membership => membership.Id == "dark"
                ? new Membership("dark", ["irrelevant-dark", .. membership.EntryIds]) : membership),
            goals: [new("dark-b", [Entry("dark-b", 1, 1)], groupId: "g"), Goal("dark-ab", Entry("dark-ab", 1, 1)),
                Goal("light-ab", Entry("light-ab", 1, 1)), Goal("bound", Member("dark", 0, 1), Entry("light-b", 1, 1))]), true);
    }

    [Test]
    public void ZeroCopyEffectsDoNotCreateAnotherActiveEffectKind() {
        EngineCase fixture = Case(2, 2);
        AssertDraw(Change(fixture, entries: [.. fixture.Request.Entries, Draw("zero", 0, 3, cost: 1)]), true);
    }
}
