using System.Numerics;
using System.Reflection;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Counting;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.DrawResolution;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Search;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture, NonParallelizable]
public sealed class PhysicalSingleSourceEnumerationTest {
    [TestCase(0), TestCase(1), TestCase(2), TestCase(3)]
    public void CompletePhysicalDistributionMatchesSeparateOpeningAndDrawBatches(int variant) {
        SearchCertificateFixtures.Fixture fixture = Fixture(variant);
        Dictionary<string, BigInteger> expectedMass = new(StringComparer.Ordinal);
        Dictionary<string, BigInteger> actualMass = new(StringComparer.Ordinal);
        ExactCalculationResult expected = Calculate(
            fixture,
            PhysicalEnumerationMethod.BatchStreaming,
            new(CalculationWorkPolicy.Unlimited),
            expectedMass
        );
        SearchCountingDiagnostics diagnostics = new();
        WorkBudget budget = new(CalculationWorkPolicy.Unlimited) { SearchDiagnostics = diagnostics };
        ExactCalculationResult actual = Calculate(fixture, PhysicalEnumerationMethod.SingleSource, budget, actualMass);
        Assert.That(diagnostics.Counts.GetValueOrDefault("primary/physical single-source frames"), Is.EqualTo(2));
        Assert.That(actualMass, Is.EquivalentTo(expectedMass), "Every held/remaining/hidden/OPT outcome has identical labeled mass.");
        Assert.That(Rows(actual), Is.EqualTo(Rows(expected)));
        Assert.That(budget.PeakCells, Is.LessThanOrEqualTo(CalculationStoragePolicy.Standard.CellLimit));
        Assert.That(budget.PeakEntries, Is.LessThanOrEqualTo(CalculationStoragePolicy.Standard.EntryLimit));
    }

    [TestCase("empty"), TestCase("minimum"), TestCase("capacity"), TestCase("cost"), TestCase("repeatable"), TestCase("limit-two"), TestCase("second")]
    public void IneligibleInputsRetainTheFullBatchFallback(string boundary) {
        SearchCertificateFixtures.Fixture original = Fixture(0);
        List<Card> cards = original.Deck.ToList();
        Card source = cards[0];
        int handSize = original.Hand;
        switch (boundary) {
            case "empty":
                handSize = 0;
                break;
            case "minimum":
                cards[0] = new([], id: source.Id, drawCount: 2, postDrawDiscardCount: 1, minimumHandSize: handSize + 1);
                break;
            case "capacity":
                handSize = cards.Sum(card => card.Copies) - 1;
                break;
            case "cost":
                cards[0] = new([], id: source.Id, drawCount: 2, preDrawRemovalCount: 1, postDrawDiscardCount: 1);
                break;
            case "repeatable":
            case "limit-two":
                cards[0] = new([], 2, id: source.Id, drawCount: 2, drawOncePerTurn: false,
                    postDrawDiscardCount: 1, activationLimit: boundary == "limit-two" ? 2 : null);
                break;
            case "second":
                cards.Add(new([], id: "other-draw", drawCount: 1, firstActivationOnly: true));
                break;
        }
        SearchCertificateFixtures.Fixture fixture = original with { Deck = cards, Hand = handSize };
        SearchCountingDiagnostics diagnostics = new();
        ExactCalculationResult expected = Calculate(fixture, PhysicalEnumerationMethod.BatchStreaming, new(CalculationWorkPolicy.Unlimited));
        ExactCalculationResult actual = Calculate(
            fixture,
            PhysicalEnumerationMethod.SingleSource,
            new(CalculationWorkPolicy.Unlimited) { SearchDiagnostics = diagnostics }
        );
        Assert.That(diagnostics.Counts.GetValueOrDefault("primary/physical single-source frames"), Is.Zero);
        Assert.That(Rows(actual), Is.EqualTo(Rows(expected)));
    }

    [TestCase(1, 0), TestCase(2, 0), TestCase(3, 0)]
    [TestCase(2, 1), TestCase(3, 1), TestCase(2, 2), TestCase(3, 2)]
    [TestCase(2, 3), TestCase(3, 3)]
    public void SourceCopyQuotientPreservesPhysicalDistributionAndFailureMass(int sourceCopies, int restriction) {
        SearchCertificateFixtures.Fixture fixture = MultiCopyFixture(sourceCopies, restriction);
        Dictionary<string, BigInteger> expectedMass = new(StringComparer.Ordinal);
        Dictionary<string, BigInteger> actualMass = new(StringComparer.Ordinal);
        ExactCalculationResult expected = Calculate(fixture, PhysicalEnumerationMethod.BatchStreaming,
            new(CalculationWorkPolicy.Unlimited), expectedMass);
        SearchCountingDiagnostics diagnostics = new();
        WorkBudget budget = new(CalculationWorkPolicy.Unlimited) { SearchDiagnostics = diagnostics };
        ExactCalculationResult actual = Calculate(fixture, PhysicalEnumerationMethod.SingleSource, budget, actualMass);
        Assert.That(actualMass, Is.EquivalentTo(expectedMass), "Held, unseen, hidden and activation state retain identical labeled mass.");
        Assert.That(Rows(actual), Is.EqualTo(Rows(expected)));
        Assert.That(diagnostics.Counts["primary/physical single-source frames"], Is.EqualTo(sourceCopies + 1));

        // The labeled reference models OPT and timing restrictions independently.
        // Its older scheduler lacks explicit activation limits, so that boundary
        // uses the full production batch route above rather than a false oracle.
        if (restriction != 3) {
            SearchCertificateOracle.Counts oracle = SearchCertificateOracle.Enumerate(
                fixture.Deck, fixture.Hand, fixture.Combos, fixture.Groups, collapseIdenticalCopies: false);
            ExactProbability[] rows = Rows(actual);
            for (int row = 0; row < rows.Length; row++) {
                Assert.That(rows[row].Numerator * oracle.Outcomes,
                    Is.EqualTo(oracle.Wins[1][row] * rows[row].Denominator), $"labeled physical row {row}");
            }
            Assert.That(actualMass.Values.Aggregate(BigInteger.Zero, (sum, mass) => sum + mass) + oracle.Failed,
                Is.EqualTo(oracle.Outcomes), "Successful and mandatory-failure mass cover every labeled permutation.");
            if (restriction == 2) {
                Assert.That(oracle.Failed, Is.GreaterThan(BigInteger.Zero));
            }
        }
        Assert.That(budget.PeakCells, Is.LessThanOrEqualTo(CalculationStoragePolicy.Standard.CellLimit));
        Assert.That(budget.PeakEntries, Is.LessThanOrEqualTo(CalculationStoragePolicy.Standard.EntryLimit));
    }

    [Test]
    public void CombinedSampleAvoidsReplayingOpeningDrawSplits() {
        CategoryBase target = new("Target");
        List<Card> cards = Enumerable.Range(0, 8).Select(index => new Card([target], id: "t" + index)).ToList();
        cards.Add(new([], id: "draw", drawCount: 2, postDrawDiscardCount: 1));
        SearchCertificateFixtures.Fixture fixture = new(
            "combined-sample",
            cards,
            5,
            [new([new(target, 4, 5)]), new([], cards: [new("t0", 0, 0)])],
            []
        );
        SearchCountingDiagnostics separateDiagnostics = new();
        SearchCountingDiagnostics combinedDiagnostics = new();
        WorkBudget separate = new(CalculationWorkPolicy.Unlimited) { SearchDiagnostics = separateDiagnostics };
        WorkBudget combined = new(CalculationWorkPolicy.Unlimited) { SearchDiagnostics = combinedDiagnostics };
        ExactCalculationResult expected = Calculate(fixture, PhysicalEnumerationMethod.BatchStreaming, separate);
        ExactCalculationResult actual = Calculate(fixture, PhysicalEnumerationMethod.SingleSource, combined);
        Assert.That(Rows(actual), Is.EqualTo(Rows(expected)));
        Assert.That(combined.Spent, Is.LessThan(separate.Spent));
        // C(8,5) absent-source terminals plus C(8,6) present-source terminals.
        // Each present-source terminal formerly appeared C(6,4) = 15 times.
        Assert.That(combinedDiagnostics.Counts["primary/physical visible terminals"], Is.EqualTo(56 + 28));
        Assert.That(separateDiagnostics.Counts["primary/physical visible terminals"], Is.EqualTo(56 + 15 * 28));
    }

    [Test]
    public void CombinedEnumerationHonorsExactWorkAndRetainedStorageThresholds() {
        SearchCertificateFixtures.Fixture fixture = Fixture(2);
        WorkBudget measured = new(CalculationWorkPolicy.Unlimited);
        ExactCalculationResult expected = Calculate(fixture, PhysicalEnumerationMethod.SingleSource, measured);
        WorkBudget exactWork = new(new(measured.Spent));
        Assert.That(Rows(Calculate(fixture, PhysicalEnumerationMethod.SingleSource, exactWork)), Is.EqualTo(Rows(expected)));
        Assert.That(exactWork.Spent, Is.EqualTo(measured.Spent));
        ProbabilityCalculationLimitException workFailure = Assert.Throws<ProbabilityCalculationLimitException>(
            () => Calculate(fixture, PhysicalEnumerationMethod.SingleSource, new(new(measured.Spent - 1)))
        )!;
        Assert.That(workFailure.Reason, Is.EqualTo(ProbabilityCalculationLimitReason.Work));
        CalculationStoragePolicy exactStorage = Storage(measured.PeakEntries, measured.PeakCells);
        Assert.That(
            Rows(Calculate(fixture, PhysicalEnumerationMethod.SingleSource, new(CalculationWorkPolicy.Unlimited, exactStorage))),
            Is.EqualTo(Rows(expected))
        );
        foreach (bool cells in new[] { false, true }) {
            CalculationStoragePolicy tooSmall = Storage(measured.PeakEntries - (cells ? 0 : 1), measured.PeakCells - (cells ? 1 : 0));
            ProbabilityCalculationLimitException storageFailure = Assert.Throws<ProbabilityCalculationLimitException>(
                () => Calculate(fixture, PhysicalEnumerationMethod.SingleSource, new(CalculationWorkPolicy.Unlimited, tooSmall))
            )!;
            Assert.That(storageFailure.Reason, Is.EqualTo(ProbabilityCalculationLimitReason.Storage));
        }
    }

    [Test]
    public void DecodedSearchPredicatesPreserveHighRowsDistinctRequirementsAndRecompilation() {
        CategoryBase a = new("A");
        CategoryBase b = new("B");
        List<Card> cards = Enumerable.Range(0, 66).Select(index => new Card([], id: "row-" + index)).ToList();
        cards[0] = new([a], id: "row-0");
        cards[32] = new([b], id: "row-32");
        cards[64] = new([a, b], id: "row-64");
        List<Combo> combos = [
            new([new(a, 1, 5), new(b, 1, 5)]), new([new(a, 1, 1)]), new([new(a, 0, 0)]),
            new([], cards: [new("row-64", 1, 5)]), new([], "Always")
        ];
        PhysicalSearchHandCounter counter = new(cards, combos, new(CalculationWorkPolicy.Unlimited));
        int[] hand = new int[cards.Count];
        int[] available = new int[cards.Count];
        hand[64] = 1;
        Assert.That(counter.Evaluate(hand, available), Is.EqualTo(new[] { false, true, false, true, true }));
        hand[0] = 1;
        Assert.That(counter.Evaluate(hand, available), Is.EqualTo(new[] { true, false, false, true, true }));
        hand[0] = 0;
        hand[64] = 0;
        hand[32] = 1;
        Assert.That(counter.Evaluate(hand, available), Is.EqualTo(new[] { false, false, true, false, true }));
    }

    private static SearchCertificateFixtures.Fixture Fixture(int variant) {
        CategoryBase target = new("Target");
        CategoryBase other = new("Other");
        return new(
            "single-" + variant,
            [
                new(
                    [target],
                    id: "draw",
                    drawCount: 2,
                    drawOncePerTurn: variant % 2 == 0,
                    postDrawDiscardCount: variant == 3 ? 2 : 1,
                    postDrawSelector: variant == 3 ? CardSelector.For(other) : CardSelector.Any,
                    preDrawDeckRemovalCount: variant == 2 ? 1 : 0,
                    postDrawDeckMillCount: variant > 0 ? 1 : 0,
                    firstActivationOnly: variant == 1,
                    blocksLaterDraws: variant == 2,
                    minimumHandSize: variant == 1 ? 3 : 0
                ),
                new([], id: "search", search: new(CardSelector.For(target), false)),
                new([target], 2, "same", id: "a"), new([target], 1, "same", id: "b"),
                new([other], id: "other"), new([], 2, id: "filler")
            ],
            3,
            [
                new([new(target, 1, 2)], groupId: "g"), new([new(target, 0, 0)], groupId: "g"),
                new([], cards: [new("draw", 1, 3)]), new([], cards: [new("filler", 0, 0)]), new([], "Completed")
            ],
            [new("g", "Either"), new("empty", "Empty")],
            variant == 1 ? ["draw"] : null
        );
    }

    private static SearchCertificateFixtures.Fixture MultiCopyFixture(int copies, int restriction) {
        CategoryBase target = new("Target");
        CategoryBase other = new("Other");
        return new(
            "source-copies-" + copies + "-" + restriction,
            [
                new([other], copies, id: "draw", drawCount: 2,
                    drawOncePerTurn: restriction == 0,
                    activationLimit: restriction == 3 ? 1 : null,
                    firstActivationOnly: restriction == 1,
                    blocksLaterDraws: restriction == 2,
                    preDrawDeckRemovalCount: restriction == 1 ? 1 : 0,
                    postDrawDeckMillCount: restriction >= 2 ? 1 : 0,
                    postDrawDiscardCount: restriction == 2 ? 2 : 1,
                    postDrawSelector: restriction == 2 ? CardSelector.For(target) : CardSelector.Any),
                new([], id: "search", search: new(CardSelector.For(target), false)),
                new([target], 2, id: "target"),
                new([target, other], id: "both"),
                new([], 3 - copies, id: "filler")
            ],
            2,
            [
                new([new(target, 1, 2)], groupId: "targets"),
                new([new(target, 0, 0)], groupId: "targets"),
                new([new(target, 1, 3), new(other, 1, 3)], groupId: "overlap"),
                new([], cards: [new("draw", 1, 3)]),
                new([], cards: [new("draw", 0, 0)]),
                new([], "Completed")
            ],
            [new("targets", "Targets"), new("overlap", "Overlap"), new("empty", "Empty")]
        );
    }

    private static ExactCalculationResult Calculate(
        SearchCertificateFixtures.Fixture fixture,
        PhysicalEnumerationMethod method,
        WorkBudget budget,
        Dictionary<string, BigInteger>? distribution = null
    ) {
        DrawActivationPriority? priority = fixture.Priority is null ? null : new(DrawActivationMode.CustomPriority, fixture.Priority);
        PostDrawCalculation calculation = new(fixture.Deck, fixture.Hand, budget, priority, deferIdentities: false) {
            ObservePhysicalOutcome = distribution is null ? null : (rows, state, available, mass) => {
                // Both routes use the same original row ordering. Include every
                // phase/OPT/hidden slot, not merely the final hand's successes.
                string key = string.Join(",", state) + "|" + string.Join(",", available);
                distribution[key] = distribution.GetValueOrDefault(key) + mass;
                Array.Fill(state, -1);
                Array.Fill(available, -1);
            }
        };
        return calculation.CalculateSearch(
            fixture.Combos,
            fixture.Groups,
            false,
            new(fixture.Deck, budget),
            physical: true,
            method: method,
            coalesceOrdinary: false
        );
    }

    private static CalculationStoragePolicy Storage(long entries, long cells) {
        ConstructorInfo constructor = typeof(CalculationStoragePolicy).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            [typeof(CalculationStorageTier), typeof(long), typeof(long)],
            null
        )!;
        return (CalculationStoragePolicy)constructor.Invoke([CalculationStorageTier.Standard, entries, cells]);
    }

    private static ExactProbability[] Rows(ExactCalculationResult result) => [result.Total, .. result.Combos, .. result.Groups];
}
