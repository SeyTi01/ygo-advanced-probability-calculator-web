using System.Numerics;
using System.Reflection;
using CardProbability.EngineTests.Fixtures;
using CardProbability.Internal;
using CardProbability.Internal.Counting;
using CardProbability.Internal.DrawResolution;
using CardProbability.Internal.Model;
using CardProbability.Internal.Resources;
using CardProbability.Internal.Search;
using Core = CardProbability.Internal.Contracts;

namespace CardProbability.EngineTests.Optimizations;

[TestFixture, NonParallelizable]
public sealed class PhysicalEnumerationTest {
    private static readonly PhysicalEnumerationMethod[] Methods = [
        PhysicalEnumerationMethod.Streaming, PhysicalEnumerationMethod.BatchStreaming,
        PhysicalEnumerationMethod.Frontier, PhysicalEnumerationMethod.SingleSource
    ];

    [Test]
    public void SystematicCorpusMatchesLabeledOracleWithoutRowPooling() {
        foreach (EngineCase fixture in SearchCertificateFixtures.Fixtures()) {
            SearchCertificateOracle.Counts oracle = SearchCertificateOracle.Enumerate(fixture);
            foreach (PhysicalEnumerationMethod method in Methods) {
                ExactCalculationResult result = Calculate(fixture, method, new(Core.CalculationWorkPolicy.Unlimited), out PostDrawCalculation execution);
                AssertRows(result, oracle.Wins[1], oracle.Outcomes, fixture.Name + "/" + method);
                Assert.That(execution.ReducedRows, Is.EqualTo(fixture.Request.Entries.Count(entry => entry.Copies > 0)));
            }
        }
    }

    [TestCase("overlapping-targets"), TestCase("filtered-failure"), TestCase("hidden-before-and-after")]
    [TestCase("searcher-targets-searcher"), TestCase("minimum-equal"), TestCase("minimum-too-high")]
    [TestCase("capacity-equal"), TestCase("capacity-too-short"), TestCase("empty-opener")]
    [TestCase("hand-cost-fallback"), TestCase("multiple-source-copies-fallback")]
    public void CombinedDrawsAndLossCountingMatchIndependentPhysicalOrders(string name) {
        EngineCase fixture = HardCase(name);
        SearchCertificateOracle.Counts oracle = SearchCertificateOracle.Enumerate(fixture);
        foreach (bool pool in new[] { false, true }) {
            foreach (PhysicalEnumerationMethod method in Methods) {
                ExactCalculationResult result = Calculate(fixture, method, new(Core.CalculationWorkPolicy.Unlimited), out _, pool);
                AssertRows(result, oracle.Wins[1], oracle.Outcomes, name + "/" + method + "/pool=" + pool);
            }
        }
        if (name == "filtered-failure") {
            Assert.That(oracle.Failed, Is.GreaterThan(BigInteger.Zero));
            Assert.That(oracle.Failed, Is.LessThan(oracle.Outcomes));
        }
    }

    [TestCase("overlapping-targets"), TestCase("filtered-failure"), TestCase("hidden-before-and-after")]
    [TestCase("hand-cost-fallback"), TestCase("multiple-source-copies-fallback"), TestCase("queued-duplicates-newly-payable")]
    [TestCase("same-batch-max-one-duplicates"), TestCase("first-only-effect-order-encounter")]
    [TestCase("first-only-effect-order-d-e"), TestCase("first-only-effect-order-e-d"), TestCase("first-only-effect-order-s")]
    [TestCase("hidden-pre-post-and-mandatory-failure")]
    public void CompletePhysicalMassPreservesQueuedDuplicatesCostsHiddenLossAndFailure(string name) {
        EngineCase fixture = HardCase(name);
        (Dictionary<string, BigInteger> expected, BigInteger failed, BigInteger total) = PhysicalOracle(fixture);
        Dictionary<string, BigInteger>? reference = null;
        foreach (PhysicalEnumerationMethod method in Methods) {
            Dictionary<string, BigInteger> complete = new(StringComparer.Ordinal);
            Dictionary<string, BigInteger> physical = new(StringComparer.Ordinal);
            Calculate(fixture, method, new(Core.CalculationWorkPolicy.Unlimited), out _, observer: (cards, state, available, mass) => {
                OrderedDrawStateLayout layout = new(cards);
                Assert.That(state[layout.DrawsLeft], Is.Zero);
                Assert.That(state[layout.CompletionSource], Is.Zero);
                int hidden = layout.HasHiddenCount ? state[layout.HiddenCount] : 0;
                Assert.That(state.Take(cards.Count).Sum() - available.Sum(), Is.EqualTo(hidden));
                Add(physical, PhysicalKey(cards.Select(card => card.Id), state.AsSpan(layout.HandStart, cards.Count).ToArray(), available), mass);
                Add(complete, string.Join(',', state) + "|" + string.Join(',', available), mass);
                Array.Fill(state, int.MinValue);
                Array.Fill(available, int.MinValue);
            });
            Assert.That(physical, Is.EquivalentTo(expected), name + "/" + method);
            Assert.That(physical.Values.Aggregate(BigInteger.Zero, (sum, mass) => sum + mass) + failed, Is.EqualTo(total));
            if (reference is not null) {
                Assert.That(complete, Is.EquivalentTo(reference), name + "/complete terminal coordinates/" + method);
            }
            reference = complete;
        }
    }

    [TestCase(0), TestCase(1), TestCase(2), TestCase(3), TestCase(4), TestCase(5)]
    public void EmptyAndExhaustionSensitiveHandsPreserveAllPhysicalOutcomes(int handSize) {
        EngineCase original = HardCase("overlapping-targets");
        EngineCase fixture = original with { Request = new(original.Request.Entries, handSize, original.Request.Goals,
            original.Request.Memberships, original.Request.Groups) };
        SearchCertificateOracle.Counts oracle = SearchCertificateOracle.Enumerate(fixture);
        foreach (PhysicalEnumerationMethod method in Methods) {
            AssertRows(Calculate(fixture, method, new(Core.CalculationWorkPolicy.Unlimited), out _), oracle.Wins[1], oracle.Outcomes, "hand=" + handSize);
        }
    }

    [Test]
    public void FirstOnlySourceOrderRemainsObservableEvenWithOrdinaryShuffles() {
        EngineCase fixture = new("first-only", new([
            new("first", 1, new(DrawCount: 1, FirstActivationOnly: true)), new("later", 1, new(DrawCount: 1)),
            new("target", 1), new("filler", 1)
        ], 2, [new("filler", [Requirement.OfEntry("filler", 1, 4)])]), new());
        OracleModel oracle = new(fixture.Request);
        DeckEntry[] entries = fixture.Request.Entries.ToArray();
        List<DeckEntry>? first = DrawSequenceOracle.Resolve(fixture, oracle, entries, out _);
        List<DeckEntry>? later = DrawSequenceOracle.Resolve(fixture, oracle, [entries[1], entries[0], entries[2], entries[3]], out _);
        Assert.That(first!.Select(entry => entry.Id), Is.EquivalentTo(new[] { "target", "filler" }));
        Assert.That(later!.Select(entry => entry.Id), Is.EquivalentTo(new[] { "first", "target" }));
        SearchCertificateOracle.Counts truth = SearchCertificateOracle.Enumerate(fixture);
        foreach (PhysicalEnumerationMethod method in Methods) {
            AssertRows(Calculate(fixture, method, new(Core.CalculationWorkPolicy.Unlimited), out _), truth.Wins[1], truth.Outcomes, method.ToString());
        }
    }

    [Test]
    public void HigherEntryAllowanceAndNewlyMetHandMinimumMatchIndependentSimpleDrawOracle() {
        EngineCase fixture = new("bounded-draws", new([
            new("d", 3, new(DrawCount: 2, ActivationLimit: 2)), new("e", 1, new(DrawCount: 1, MinimumHandSize: 3)),
            new("target", 1), new("filler", 3)
        ], 2, [new("positive", [Requirement.InMembership("target", 1, 1)], groupId: "g"),
            new("zero", [Requirement.InMembership("target", 0, 0)], groupId: "g"),
            new("source", [Requirement.OfEntry("d", 1, 3)])], [new("target", ["target"])], ["g", "empty"]), new());
        DrawSequenceOracle.Counts truth = DrawSequenceOracle.Enumerate(fixture);
        foreach (PhysicalEnumerationMethod method in Methods) {
            AssertRows(Calculate(fixture, method, new(Core.CalculationWorkPolicy.Unlimited), out _), truth.Wins, truth.Total, method.ToString());
        }
    }

    [Test]
    public void EntryOrderDoesNotEraseDirectIdentityOrEmptyGroups() {
        EngineCase fixture = HardCase("overlapping-targets");
        SearchCertificateOracle.Counts truth = SearchCertificateOracle.Enumerate(fixture);
        EngineCase reversed = fixture with { Request = new(fixture.Request.Entries.Reverse(), fixture.Request.HandSize,
            fixture.Request.Goals, fixture.Request.Memberships, fixture.Request.Groups) };
        SearchCertificateOracle.Counts reverseTruth = SearchCertificateOracle.Enumerate(reversed);
        Assert.That(reverseTruth.Wins[1], Is.EqualTo(truth.Wins[1]));
        ExactCalculationResult result = Calculate(reversed, PhysicalEnumerationMethod.SingleSource, new(Core.CalculationWorkPolicy.Unlimited), out _, pool: true);
        AssertRows(result, truth.Wins[1], truth.Outcomes, "reversed entries");
        Assert.That(result.Groups[^1].Numerator, Is.EqualTo(BigInteger.Zero));
    }

    [TestCase(1, 0), TestCase(2, 0), TestCase(3, 0), TestCase(2, 1), TestCase(3, 1)]
    [TestCase(2, 2), TestCase(3, 2), TestCase(2, 3), TestCase(3, 3)]
    public void SourceCopyQuotientPreservesPhysicalDistributionAndFailureMass(int sourceCopies, int restriction) {
        EngineCase fixture = SingleSource(sourceCopies, restriction);
        Dictionary<string, BigInteger>[] distributions = [new(), new()];
        ExactCalculationResult[] results = new ExactCalculationResult[2];
        SearchCountingDiagnostics diagnostics = new();
        for (int side = 0; side < 2; side++) {
            int captured = side;
            WorkBudget budget = new(Core.CalculationWorkPolicy.Unlimited) { SearchDiagnostics = side == 1 ? diagnostics : null };
            results[side] = Calculate(fixture, side == 0 ? PhysicalEnumerationMethod.BatchStreaming : PhysicalEnumerationMethod.SingleSource,
                budget, out _, observer: (_, state, available, mass) => {
                    Add(distributions[captured], string.Join(',', state) + "|" + string.Join(',', available), mass);
                    Array.Fill(state, -1);
                    Array.Fill(available, -1);
                });
        }
        Assert.That(distributions[1], Is.EquivalentTo(distributions[0]));
        Assert.That(Rows(results[1]), Is.EqualTo(Rows(results[0])));
        Assert.That(diagnostics.Counts["primary/physical single-source frames"], Is.EqualTo(sourceCopies + 1));
        SearchCertificateOracle.Counts oracle = SearchCertificateOracle.Enumerate(fixture);
        AssertRows(results[1], oracle.Wins[1], oracle.Outcomes, fixture.Name);
        Assert.That(distributions[1].Values.Aggregate(BigInteger.Zero, (sum, mass) => sum + mass) + oracle.Failed, Is.EqualTo(oracle.Outcomes));
        if (restriction == 2) {
            Assert.That(oracle.Failed, Is.GreaterThan(BigInteger.Zero));
        }
    }

    [TestCase(0), TestCase(1), TestCase(2), TestCase(3)]
    public void CompletePhysicalDistributionMatchesSeparateOpeningAndDrawBatches(int variant) {
        EngineCase fixture = SingleVariant(variant);
        Dictionary<string, BigInteger>[] distributions = [new(), new()];
        ExactCalculationResult[] results = new ExactCalculationResult[2];
        SearchCountingDiagnostics diagnostics = new();
        WorkBudget budget = new(Core.CalculationWorkPolicy.Unlimited) { SearchDiagnostics = diagnostics };
        for (int side = 0; side < 2; side++) {
            int captured = side;
            results[side] = Calculate(fixture, side == 0 ? PhysicalEnumerationMethod.BatchStreaming : PhysicalEnumerationMethod.SingleSource,
                side == 1 ? budget : new(Core.CalculationWorkPolicy.Unlimited), out _, observer: (_, state, available, mass) => {
                    Add(distributions[captured], string.Join(',', state) + "|" + string.Join(',', available), mass);
                    Array.Fill(state, -1);
                    Array.Fill(available, -1);
                });
        }
        Assert.That(diagnostics.Counts.GetValueOrDefault("primary/physical single-source frames"), Is.EqualTo(2));
        Assert.That(distributions[1], Is.EquivalentTo(distributions[0]));
        Assert.That(Rows(results[1]), Is.EqualTo(Rows(results[0])));
        Assert.That(budget.PeakCells, Is.LessThanOrEqualTo(Core.CalculationStoragePolicy.Standard.CellLimit));
        Assert.That(budget.PeakEntries, Is.LessThanOrEqualTo(Core.CalculationStoragePolicy.Standard.EntryLimit));
        SearchCertificateOracle.Counts oracle = SearchCertificateOracle.Enumerate(fixture);
        AssertRows(results[1], oracle.Wins[1], oracle.Outcomes, fixture.Name);
        Assert.That(distributions[1].Values.Aggregate(BigInteger.Zero, (sum, mass) => sum + mass) + oracle.Failed, Is.EqualTo(oracle.Outcomes));
    }

    [TestCase("empty"), TestCase("minimum"), TestCase("capacity"), TestCase("cost")]
    [TestCase("repeatable"), TestCase("limit-two"), TestCase("second")]
    public void IneligibleInputsRetainTheFullBatchFallback(string boundary) {
        EngineCase original = SingleVariant(0);
        List<DeckEntry> entries = original.Request.Entries.ToList();
        EffectPlan source = entries[0].Effect!;
        int hand = original.Request.HandSize;
        switch (boundary) {
            case "empty": hand = 0; break;
            case "minimum": source = source with { MinimumHandSize = hand + 1 }; break;
            case "capacity": hand = entries.Sum(entry => entry.Copies) - 1; break;
            case "cost": source = source with { HandCost = 1 }; break;
            case "repeatable": entries[0] = entries[0] with { Copies = 2 }; source = source with { ActivationLimit = null }; break;
            case "limit-two": entries[0] = entries[0] with { Copies = 2 }; source = source with { ActivationLimit = 2 }; break;
            case "second": entries.Add(new("second", 1, new(DrawCount: 1, FirstActivationOnly: true))); break;
        }
        entries[0] = entries[0] with { Effect = source };
        IEnumerable<Membership> memberships = original.Request.Memberships;
        if (boundary is "minimum" or "cost" or "repeatable" or "limit-two") {
            memberships = memberships.Select(membership => new Membership(membership.Id, membership.EntryIds.Where(id => id != "draw")));
        }
        EngineCase fixture = original with { Request = new(entries, hand, original.Request.Goals, memberships, original.Request.Groups) };
        SearchCountingDiagnostics diagnostics = new();
        ExactCalculationResult expected = Calculate(fixture, PhysicalEnumerationMethod.BatchStreaming, new(Core.CalculationWorkPolicy.Unlimited), out _);
        ExactCalculationResult actual = Calculate(fixture, PhysicalEnumerationMethod.SingleSource,
            new(Core.CalculationWorkPolicy.Unlimited) { SearchDiagnostics = diagnostics }, out _);
        Assert.That(diagnostics.Counts.GetValueOrDefault("primary/physical single-source frames"), Is.Zero);
        Assert.That(Rows(actual), Is.EqualTo(Rows(expected)));
    }

    [TestCase((int)PhysicalEnumerationMethod.BatchStreaming), TestCase((int)PhysicalEnumerationMethod.SingleSource)]
    public void EnumerationHonorsExactWorkAndRetainedStorageThresholds(int methodValue) {
        PhysicalEnumerationMethod method = (PhysicalEnumerationMethod)methodValue;
        EngineCase fixture = method == PhysicalEnumerationMethod.BatchStreaming ? BatchResourceCase() : SingleVariant(2);
        WorkBudget measured = new(Core.CalculationWorkPolicy.Unlimited);
        ExactCalculationResult expected = Calculate(fixture, method, measured, out PostDrawCalculation execution);
        if (method == PhysicalEnumerationMethod.BatchStreaming) {
            Assert.That(execution.LargestStructure, Is.EqualTo("physical batch enumeration stack"));
            Assert.That(execution.PeakCells, Is.EqualTo(measured.PeakCells));
        }
        WorkBudget enough = new(new(measured.Spent));
        Assert.That(Rows(Calculate(fixture, method, enough, out _)), Is.EqualTo(Rows(expected)));
        Assert.That(enough.Spent, Is.EqualTo(measured.Spent));
        Core.ProbabilityCalculationLimitException workFailure = Assert.Throws<Core.ProbabilityCalculationLimitException>(
            () => Calculate(fixture, method, new(new(measured.Spent - 1)), out _))!;
        Assert.That(workFailure.Reason, Is.EqualTo(Core.ProbabilityCalculationLimitReason.Work));
        Core.CalculationStoragePolicy exact = Storage(measured.PeakEntries, measured.PeakCells);
        Assert.That(Rows(Calculate(fixture, method, new(Core.CalculationWorkPolicy.Unlimited, exact), out _)), Is.EqualTo(Rows(expected)));
        foreach (bool cells in new[] { false, true }) {
            Core.CalculationStoragePolicy tooSmall = Storage(measured.PeakEntries - (cells ? 0 : 1), measured.PeakCells - (cells ? 1 : 0));
            Core.ProbabilityCalculationLimitException failure = Assert.Throws<Core.ProbabilityCalculationLimitException>(
                () => Calculate(fixture, method, new(Core.CalculationWorkPolicy.Unlimited, tooSmall), out _))!;
            Assert.That(failure.Reason, Is.EqualTo(Core.ProbabilityCalculationLimitReason.Storage));
        }
    }

    [Test]
    public void CombinedSampleAvoidsReplayingOpeningDrawSplits() {
        DeckEntry[] entries = [.. Enumerable.Range(0, 8).Select(index => new DeckEntry("t" + index, 1)),
            new("draw", 1, new(DrawCount: 2, RemovalCount: 1))];
        EngineCase fixture = new("combined", new(entries, 5,
            [new("four", [Requirement.InMembership("target", 4, 5)]), new("exclude", [Requirement.OfEntry("t0", 0, 0)])],
            [new("target", entries.Take(8).Select(entry => entry.Id))]), new());
        SearchCountingDiagnostics separateDiagnostics = new();
        SearchCountingDiagnostics combinedDiagnostics = new();
        WorkBudget separate = new(Core.CalculationWorkPolicy.Unlimited) { SearchDiagnostics = separateDiagnostics };
        WorkBudget combined = new(Core.CalculationWorkPolicy.Unlimited) { SearchDiagnostics = combinedDiagnostics };
        ExactCalculationResult expected = Calculate(fixture, PhysicalEnumerationMethod.BatchStreaming, separate, out _);
        ExactCalculationResult actual = Calculate(fixture, PhysicalEnumerationMethod.SingleSource, combined, out _);
        Assert.That(Rows(actual), Is.EqualTo(Rows(expected)));
        Assert.That(combined.Spent, Is.LessThan(separate.Spent));
        Assert.That(combinedDiagnostics.Counts["primary/physical visible terminals"], Is.EqualTo(56 + 28));
        Assert.That(separateDiagnostics.Counts["primary/physical visible terminals"], Is.EqualTo(56 + 15 * 28));
    }

    [Test]
    public void BatchQuotientAvoidsReplayingDistinctOrdinaryOpeningOrders() {
        DeckEntry[] entries = [.. Enumerable.Range(0, 7).Select(index => new DeckEntry("t" + index, 1)), new("draw", 1, new(DrawCount: 2))];
        EngineCase fixture = new("batch-quotient", new(entries, 5,
            [new("four", [Requirement.InMembership("target", 4, 6)]), new("direct", [Requirement.OfEntry("t0", 1, 6)])],
            [new("target", entries.Take(7).Select(entry => entry.Id))]), new());
        WorkBudget streaming = new(Core.CalculationWorkPolicy.Unlimited);
        WorkBudget batch = new(Core.CalculationWorkPolicy.Unlimited);
        ExactCalculationResult expected = Calculate(fixture, PhysicalEnumerationMethod.Streaming, streaming, out _);
        ExactCalculationResult actual = Calculate(fixture, PhysicalEnumerationMethod.BatchStreaming, batch, out _);
        Assert.That(Rows(actual), Is.EqualTo(Rows(expected)));
        Assert.That(batch.Spent, Is.LessThan(streaming.Spent));
    }

    [TestCase(false), TestCase(true)]
    public void PackedPhysicalTerminalsPreserveEveryMassAndOracleRow(bool frontier) {
        bool reduced = false;
        foreach (EngineCase fixture in SearchCertificateFixtures.Fixtures()) {
            PreparedCalculation prepared = fixture.Prepare();
            Dictionary<string, BigInteger>[] terminals = [new(), new()];
            ExactCalculationResult[] results = new ExactCalculationResult[2];
            WorkBudget[] budgets = [new(Core.CalculationWorkPolicy.Interactive), new(Core.CalculationWorkPolicy.Interactive)];
            for (int side = 0; side < 2; side++) {
                int captured = side;
                PostDrawCalculation calculation = new(prepared.Cards, fixture.Request.HandSize, budgets[side], Priority(fixture), compactSearchTerminals: side == 1) {
                    ObserveTerminal = (values, mass) => {
                        Add(terminals[captured], string.Join(',', values), mass);
                        Array.Fill(values, int.MinValue);
                    }
                };
                results[side] = calculation.CalculateSearch(prepared.Combos, prepared.Groups, false, new(prepared.Cards, budgets[side]),
                    physical: true, method: frontier ? PhysicalEnumerationMethod.Frontier : PhysicalEnumerationMethod.Auto);
            }
            Assert.That(terminals[1], Is.EquivalentTo(terminals[0]), fixture.Name);
            Assert.That(Rows(results[1]), Is.EqualTo(Rows(results[0])), fixture.Name);
            SearchCertificateOracle.Counts oracle = SearchCertificateOracle.Enumerate(fixture);
            AssertRows(results[1], oracle.Wins[1], oracle.Outcomes, fixture.Name);
            reduced |= budgets[1].PeakCells < budgets[0].PeakCells;
        }
        Assert.That(reduced, Is.True);
    }

    internal static EngineCase HardCase(string name) {
        if (name.StartsWith("first-only-effect-order-", StringComparison.Ordinal) || name is "same-batch-max-one-duplicates" or "hidden-pre-post-and-mandatory-failure") {
            string? suffix = name.StartsWith("first-only-effect-order-", StringComparison.Ordinal) ? name[24..] : null;
            bool hiddenFailure = name == "hidden-pre-post-and-mandatory-failure";
            DeckEntry[] boundaryEntries = [new("d", name == "same-batch-max-one-duplicates" ? 2 : 1,
                    new(DrawCount: 1, FirstActivationOnly: suffix is not null, HiddenDeckCost: hiddenFailure ? 1 : 0,
                        HiddenPostDrawMill: hiddenFailure ? 1 : 0, RemovalCount: hiddenFailure ? 1 : 0,
                        RemovalEligibility: hiddenFailure ? new("target") : null)),
                new("e", 1, new(DrawCount: name == "same-batch-max-one-duplicates" ? 2 : 1, BlocksLaterDraws: hiddenFailure)),
                new("t", 1), new("s", 1, new(Search: new(new("target")))), new("f", hiddenFailure ? 3 : 2)];
            string[]? priority = suffix switch { "d-e" => ["d", "e"], "e-d" => ["e", "d"], "s" => ["s"], _ => null };
            if (name == "same-batch-max-one-duplicates") { priority = ["e", "d"]; }
            if (hiddenFailure) { priority = ["d", "e"]; }
            return new(name, new(boundaryEntries, 2, [new("one-two", [Requirement.InMembership("target", 1, 2)], groupId: "g"),
                new("zero", [Requirement.InMembership("target", 0, 0)], groupId: "g"),
                new("no-filler", [Requirement.OfEntry("f", 0, 0)]), new("completed", [], groupId: "complete")],
                [new("target", ["t"])], ["g", "complete", "empty"]),
                priority is null ? new() : new(activationOrder: ActivationOrder.Priority, priorityEntryIds: priority));
        }
        if (name == "queued-duplicates-newly-payable") {
            return new(name, new([
                new("d", 2, new(DrawCount: 1, HandCost: 1, HandCostEligibility: new("material"))), new("e", 1, new(DrawCount: 2)),
                new("m", 1), new("t", 1), new("s", 1, new(Search: new(new("target")))), new("f", 2)
            ], 2, [new("one-two", [Requirement.InMembership("target", 1, 2)], groupId: "g"),
                new("zero", [Requirement.InMembership("target", 0, 0)], groupId: "g"),
                new("no-filler", [Requirement.OfEntry("f", 0, 0)]), new("completed", [], groupId: "complete")],
                [new("target", ["t"]), new("material", ["m"])], ["g", "complete", "empty"]), new());
        }
        List<DeckEntry> entries = [new("draw", 1, new(DrawCount: 2, RemovalCount: 1, HiddenPostDrawMill: 1)),
            new("search-a", 1, new(Search: new(new("first")))), new("search-b", 1, new(Search: new(new("second")))),
            new("target-a", 1), new("target-b", 1), new("target-both", 1), new("filler-a", 1), new("filler-b", 1)];
        int hand = 3;
        EffectPlan effect = entries[0].Effect!;
        switch (name) {
            case "filtered-failure": effect = effect with { RemovalCount = 2, RemovalEligibility = new("first") }; break;
            case "hidden-before-and-after": effect = effect with { HiddenDeckCost = 1, FirstActivationOnly = true, BlocksLaterDraws = true }; break;
            case "searcher-targets-searcher": entries[1] = entries[1] with { Effect = new(Search: new(new("searcher"))) }; break;
            case "minimum-equal": effect = effect with { MinimumHandSize = hand }; break;
            case "minimum-too-high": effect = effect with { MinimumHandSize = hand + 1 }; break;
            case "capacity-equal": entries.RemoveAt(entries.Count - 1); effect = effect with { HiddenPostDrawMill = 2 }; break;
            case "capacity-too-short": entries.RemoveRange(entries.Count - 2, 2); effect = effect with { HiddenPostDrawMill = 2 }; break;
            case "empty-opener": hand = 0; break;
            case "hand-cost-fallback": effect = effect with { HandCost = 1, HandCostEligibility = new("payment") }; break;
            case "multiple-source-copies-fallback": entries[0] = entries[0] with { Copies = 2 }; entries.RemoveAt(entries.Count - 1); break;
        }
        entries[0] = entries[0] with { Effect = effect };
        return new(name, new(entries, hand, Goals(), [new("first", ["draw", "target-a", "target-both"]),
            new("second", ["target-b", "target-both"]), new("payment", ["target-a"]), new("searcher", ["search-a", "search-b"])],
            ["pieces", "identity", "complete", "empty"]), new());
    }

    private static Goal[] Goals() => [
        new("distinct", [Requirement.InMembership("first", 1, 2), Requirement.InMembership("second", 1, 2)], groupId: "pieces"),
        new("exclude", [Requirement.InMembership("first", 0, 0)], groupId: "pieces"),
        new("identity", [Requirement.OfEntry("target-a", 1, 3), Requirement.OfEntry("target-b", 0, 0)], groupId: "identity"),
        new("source", [Requirement.OfEntry("draw", 0, 0)], groupId: "identity"), new("completed", [], groupId: "complete")
    ];

    internal static EngineCase SingleSource(int copies, int restriction) {
        EffectPlan effect = new(DrawCount: 2, ActivationLimit: restriction is 0 or 3 ? 1 : null,
            FirstActivationOnly: restriction == 1, BlocksLaterDraws: restriction == 2,
            HiddenDeckCost: restriction == 1 ? 1 : 0, HiddenPostDrawMill: restriction >= 2 ? 1 : 0,
            RemovalCount: restriction == 2 ? 2 : 1, RemovalEligibility: restriction == 2 ? new("target") : null);
        return new("copies-" + copies + "-restriction-" + restriction, new([
            new("draw", copies, effect), new("search", 1, new(Search: new(new("target")))), new("target", 2),
            new("both", 1), new("filler", 3 - copies)], 2, [
                new("positive", [Requirement.InMembership("target", 1, 2)], groupId: "targets"),
                new("zero", [Requirement.InMembership("target", 0, 0)], groupId: "targets"),
                new("distinct", [Requirement.InMembership("target", 1, 3), Requirement.InMembership("other", 1, 3)], groupId: "overlap"),
                new("source-held", [Requirement.OfEntry("draw", 1, 3)]), new("source-absent", [Requirement.OfEntry("draw", 0, 0)]), new("completed", [])
            ], [new("target", ["target", "both"]), new("other", ["draw", "both"])], ["targets", "overlap", "empty"]), new());
    }

    private static EngineCase BatchResourceCase() => new("deep-small-stack", new([
        new("d", 8, new(DrawCount: 1, ActivationLimit: null)), new("t", 1), new("f", 1),
        new("s", 1, new(Search: new(new("target"))))
    ], 1, [new("target", [Requirement.InMembership("target", 1, 1)])], [new("target", ["t"])]), new());

    private static EngineCase SingleVariant(int variant) => new("single-" + variant, new([
        new("draw", 1, new(DrawCount: 2, ActivationLimit: variant % 2 == 0 ? 1 : null,
            RemovalCount: variant == 3 ? 2 : 1, RemovalEligibility: variant == 3 ? new("other") : null,
            HiddenDeckCost: variant == 2 ? 1 : 0, HiddenPostDrawMill: variant > 0 ? 1 : 0,
            FirstActivationOnly: variant == 1, BlocksLaterDraws: variant == 2, MinimumHandSize: variant == 1 ? 3 : 0)),
        new("search", 1, new(Search: new(new("target")))), new("a", 2), new("b", 1), new("other", 1), new("filler", 2)
    ], 3, [new("one-two", [Requirement.InMembership("target", 1, 2)], groupId: "g"),
        new("zero", [Requirement.InMembership("target", 0, 0)], groupId: "g"), new("source", [Requirement.OfEntry("draw", 1, 3)]),
        new("no-filler", [Requirement.OfEntry("filler", 0, 0)]), new("completed", [])],
        [new("target", ["draw", "a", "b"]), new("other", ["other"])], ["g", "empty"]),
        variant == 1 ? new(activationOrder: ActivationOrder.Priority, priorityEntryIds: ["draw"]) : new());

    internal static ExactCalculationResult Calculate(EngineCase fixture, PhysicalEnumerationMethod method, WorkBudget budget,
        out PostDrawCalculation execution, bool pool = false, Action<IReadOnlyList<Card>, int[], int[], BigInteger>? observer = null) {
        PreparedCalculation prepared = fixture.Prepare();
        execution = new(prepared.Cards, fixture.Request.HandSize, budget, Priority(fixture), deferIdentities: false) { ObservePhysicalOutcome = observer };
        return execution.CalculateSearch(prepared.Combos, prepared.Groups, false, new(prepared.Cards, budget), physical: true, method: method, coalesceOrdinary: pool);
    }

    internal static DrawActivationPriority? Priority(EngineCase fixture) => fixture.Options.ActivationOrder == ActivationOrder.Priority
        ? new(DrawActivationMode.CustomPriority, fixture.Options.PriorityEntryIds) : null;

    internal static Core.CalculationStoragePolicy Storage(long entries, long cells) {
        ConstructorInfo constructor = typeof(Core.CalculationStoragePolicy).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
            null, [typeof(Core.CalculationStorageTier), typeof(long), typeof(long)], null)!;
        return (Core.CalculationStoragePolicy)constructor.Invoke([Core.CalculationStorageTier.Standard, entries, cells]);
    }

    internal static ExactProbability[] Rows(ExactCalculationResult result) => [result.Total, .. result.Combos, .. result.Groups];

    internal static void AssertRows(ExactCalculationResult result, BigInteger[] wins, BigInteger denominator, string context) {
        ExactProbability[] rows = Rows(result);
        Assert.That(rows, Has.Length.EqualTo(wins.Length));
        for (int row = 0; row < rows.Length; row++) {
            Assert.That(rows[row].Numerator * denominator, Is.EqualTo(wins[row] * rows[row].Denominator), context + "/row=" + row);
        }
    }

    private static (Dictionary<string, BigInteger>, BigInteger, BigInteger) PhysicalOracle(EngineCase fixture) {
        DeckEntry[] labels = fixture.Request.Entries.SelectMany(entry => Enumerable.Repeat(entry, entry.Copies)).ToArray();
        int[] permutation = Enumerable.Range(0, labels.Length).ToArray();
        OracleModel model = new(fixture.Request);
        Dictionary<string, BigInteger> mass = new(StringComparer.Ordinal);
        BigInteger failed = 0;
        BigInteger total = 0;
        Visit(0);
        return (mass, failed, total);

        void Visit(int position) {
            if (position == permutation.Length) {
                total++;
                List<DeckEntry>? hand = DrawSequenceOracle.Resolve(fixture, model, permutation.Select(index => labels[index]).ToArray(), out IReadOnlyList<DeckEntry> remaining);
                if (hand is null) { failed++; return; }
                string key = PhysicalKey(fixture.Request.Entries.Select(entry => entry.Id), fixture.Request.Entries.Select(entry => hand.Count(held => held.Id == entry.Id)).ToArray(),
                    fixture.Request.Entries.Select(entry => remaining.Count(available => available.Id == entry.Id)).ToArray());
                Add(mass, key, BigInteger.One);
                return;
            }
            for (int index = position; index < permutation.Length; index++) {
                (permutation[position], permutation[index]) = (permutation[index], permutation[position]);
                Visit(position + 1);
                (permutation[position], permutation[index]) = (permutation[index], permutation[position]);
            }
        }
    }

    private static string PhysicalKey(IEnumerable<string> ids, int[] held, int[] available) => string.Join(';', ids
        .Select((id, row) => (Id: id, Held: held[row], Available: available[row])).OrderBy(entry => entry.Id, StringComparer.Ordinal)
        .Select(entry => entry.Id + ":" + entry.Held + "," + entry.Available));

    private static void Add(Dictionary<string, BigInteger> distribution, string key, BigInteger mass) => distribution[key] = distribution.GetValueOrDefault(key) + mass;
}
