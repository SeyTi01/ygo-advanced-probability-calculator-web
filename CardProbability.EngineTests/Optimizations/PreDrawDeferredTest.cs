using System.Numerics;
using CardProbability.EngineTests.Fixtures;
using static CardProbability.EngineTests.Optimizations.DeferredTestCases;

namespace CardProbability.EngineTests.Optimizations;

[TestFixture]
public sealed class PreDrawDeferredTest {
    private static EngineCase Case(int copies = 1, bool opt = false, bool eligible = false, int mill = 0, int hand = 2) => Create(
        "filtered pre-cost", [Draw("source", copies, limit: opt ? 1 : null, cost: 1, costSet: "cost", mill: mill),
            new("material-ab", 1), new("material-b", 1), new("spell", 1), new("pool-ab", 1), new("pool-b", 1)], hand,
        [new("distinct", [Member("a"), Member("b")], groupId: "g"), new("intersection", [Member("ab", 1, 1)], groupId: "g"),
            new("direct-pair", [Entry("material-ab", 1, 1), Entry("pool-b", 1, 1)], groupId: "other"),
            new("source", [Entry("source", 1, 2)], groupId: "other"), Goal("material-exclusion", Member("material", 0, 0)),
            Goal("direct-exclusion", Entry("pool-ab", 0, 0)), Goal("bounded", Member("b", 0, 1), Entry("spell", 1, 1)),
            new("alternative", [], [new([Member("a", 1, 1), Entry("pool-b", 1, 1)])])],
        [new("cost", eligible ? ["source", "material-ab", "material-b"] : ["material-ab", "material-b"]),
            new("material", eligible ? ["source", "material-ab", "spell"] : ["material-ab", "spell"]),
            new("alternative", ["material-b"]), new("a", ["material-ab", "spell", "pool-ab"]),
            new("b", ["material-ab", "material-b", "pool-ab", "pool-b"]), new("ab", ["material-ab", "pool-ab"])],
        ["g", "other", "empty"]);

    private static IEnumerable<TestCaseData> Cases() {
        foreach (int copies in new[] { 1, 2, 3 }) {
            foreach (bool opt in new[] { false, true }) {
                foreach (bool eligible in new[] { false, true }) {
                    yield return new(copies, opt, eligible);
                }
            }
        }
    }

    [TestCaseSource(nameof(Cases))]
    public void FilteredCostAndHiddenMillMatchEveryLabeledResult(int copies, bool opt, bool sourceEligible) =>
        AssertDraw(Case(copies, opt, sourceEligible, copies % 3), true);

    [TestCase(0), TestCase(1), TestCase(4)]
    public void EmptyAndSourceOnlyOpenersAndLargeHandsPreserveDenominator(int hand) => AssertDraw(Case(2, mill: 1, hand: hand), true);

    [TestCase(0), TestCase(1), TestCase(2)]
    public void AbsentOrRepeatedMaterialAndTwoCardCostsStayExact(int materialCopies) {
        EngineCase fixture = Case(2, hand: 3);
        DeckEntry[] entries = fixture.Request.Entries.Where(entry => entry.Id != "material-b").Select(entry => entry.Id switch {
            "source" => entry with { Effect = entry.Effect! with { HandCost = 2 } },
            "material-ab" => entry with { Copies = materialCopies },
            _ => entry
        }).ToArray();
        Membership[] memberships = fixture.Request.Memberships.Select(membership => new Membership(membership.Id,
            membership.EntryIds.Where(id => id != "material-b"))).ToArray();
        Assert.That(AssertDraw(Change(fixture, entries: entries, memberships: memberships), true).Failed, Is.EqualTo(BigInteger.Zero));
    }

    [TestCase(3), TestCase(4)]
    public void FullDrawAndMillCapacityIsCheckedBeforePayment(int remaining) {
        EngineCase fixture = Create("capacity-before-payment", [Draw("source", cost: 1, costSet: "cost", mill: 2),
            new("material-ab", 1), new("pool-ab", 1), new("pool-b", 1), new("spell", remaining - 2)], 2,
            [Goal("target", Member("a"))], [new("cost", ["material-ab"]), new("a", ["pool-ab", "spell"])]);
        DeckEntry[] order = fixture.Request.Entries.SelectMany(entry => Enumerable.Repeat(entry, entry.Copies)).ToArray();
        List<DeckEntry>? hand = DrawSequenceOracle.Resolve(fixture, new(fixture.Request), order, out _);
        Assert.That(hand!.Select(entry => entry.Id), remaining == 3
            ? Is.EquivalentTo(new[] { "source", "material-ab" }) : Is.EquivalentTo(new[] { "pool-ab", "pool-b" }));
        AssertDraw(fixture);
    }

    [TestCase(false), TestCase(true)]
    public void NewlyDrawnSourceAndMaterialCanActivateAfterUnseenPositions(bool enoughForSecondBatch) {
        EngineCase fixture = Create("newly payable source", [Draw("source", 2, cost: 1, costSet: "cost", mill: 2),
            new("material-ab", 2), new("pool-ab", 1), new("pool-b", enoughForSecondBatch ? 5 : 4)], 2,
            [Goal("a", Member("a"))], [new("cost", ["material-ab"]), new("a", ["pool-ab"])]);
        DeckEntry source = fixture.Request.Entries[0];
        DeckEntry material = fixture.Request.Entries[1];
        DeckEntry[] order = [source, material, source, material, fixture.Request.Entries[2],
            .. Enumerable.Repeat(fixture.Request.Entries[3], fixture.Request.Entries[3].Copies)];
        List<DeckEntry>? resolved = DrawSequenceOracle.Resolve(fixture, new(fixture.Request), order, out _);
        Assert.That(resolved!.Select(entry => entry.Id), enoughForSecondBatch
            ? Is.EquivalentTo(new[] { "pool-b", "pool-b" }) : Is.EquivalentTo(new[] { "source", "material-ab" }));
        AssertDraw(fixture);
    }

    [TestCase(0), TestCase(1)]
    public void JointFilteredPreAndPostRemovalRetainsUnionOfEligibleMaterial(int hiddenCost) {
        EngineCase fixture = Case(2, eligible: true, mill: 1);
        DrawSequenceOracle.Counts oracle = AssertDraw(Change(fixture, entries: fixture.Request.Entries.Select(entry => entry.Effect is null ? entry
            : entry with { Effect = entry.Effect with { RemovalCount = 1, RemovalEligibility = new("alternative"), HiddenDeckCost = hiddenCost } })), true);
        Assert.That(oracle.Failed, Is.GreaterThan(BigInteger.Zero));
        Assert.That(oracle.Wins[0], Is.GreaterThan(BigInteger.Zero).And.LessThan(oracle.Total));
    }

    [Test]
    public void SourceAndOrdinaryMaterialTieUsesFullComboRelevance() {
        EngineCase fixture = Case(3, eligible: true, hand: 3);
        AssertDraw(Change(fixture, entries: [fixture.Request.Entries[0], new("irrelevant-material", 1), .. fixture.Request.Entries.Skip(1)],
            memberships: fixture.Request.Memberships.Select(membership => membership.Id is "material" or "cost"
                ? new Membership(membership.Id, [.. membership.EntryIds, "irrelevant-material"]) : membership),
            goals: [Goal("source-bound", Entry("source", 0, 1)), new("material", [Entry("material-ab", 1, 1)], groupId: "g"),
                Goal("pool", Entry("pool-ab", 1, 1)), Goal("exclusion", Entry("pool-b", 0, 0))]), true);
    }

    [Test]
    public void UnsafeSelectionAndTimingShapesKeepOrderedFallback() {
        EngineCase fixture = Case(2, mill: 1);
        EffectPlan source = fixture.Request.Entries[0].Effect!;
        foreach (EffectPlan effect in new[] { source with { HandCostEligibility = null }, source with { RemovalCount = 1 },
            source with { FirstActivationOnly = true }, source with { BlocksLaterDraws = true } }) {
            AssertDraw(Change(fixture, entries: [fixture.Request.Entries[0] with { Effect = effect }, .. fixture.Request.Entries.Skip(1)]), false);
        }
        AssertDraw(Change(Case(), entries: [.. Case().Request.Entries, Draw("other", count: 1)]), true);
    }
}
