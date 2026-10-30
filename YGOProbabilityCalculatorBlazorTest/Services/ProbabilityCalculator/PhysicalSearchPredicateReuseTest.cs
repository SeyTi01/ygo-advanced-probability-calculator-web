using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Counting;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.DrawResolution;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Search;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public sealed class PhysicalSearchPredicateReuseTest {
    [TestCase(false), TestCase(true)]
    public void TerminalCachePreservesSharedTargetsEntryAllowancesAndBorrowedResults(bool oncePerTurn) {
        CategoryBase target = new("Target");
        List<Card> cards = [
            new([], 2, id: "first", search: new(CardSelector.For(target), false), oncePerTurn: oncePerTurn),
            new([], id: "second", search: new(CardSelector.For(target), false), oncePerTurn: oncePerTurn),
            new([target], 3, id: "target"), new([], 3, id: "filler")
        ];
        List<Combo> combos = [new([new(target, 2, 5)]), new([new(target, 3, 5)]), new([new(target, 0, 0)]),
            new([], cards: [new("first", 0, 0)])];
        WorkBudget budget = new(CalculationWorkPolicy.Unlimited);
        PhysicalSearchHandCounter counter = new(cards, combos, budget, memoize: true);
        for (int first = 0; first <= 2; first++) {
            for (int second = 0; second <= 1; second++) {
                for (int held = 0; held <= 1; held++) {
                    int allowance = (oncePerTurn ? Math.Min(first, 1) : first) + second;
                    for (int available = 0; available <= 3 - held; available++) {
                        int maximumTargets = held + Math.Min(available, allowance);
                        bool canRemoveFirst = first == 0 || (available >= first && (!oncePerTurn || first == 1));
                        bool[] expected = [maximumTargets >= 2, maximumTargets >= 3, held == 0, canRemoveFirst];
                        int[] hand = [first, second, held, 1];
                        // Non-target capacity changes cannot affect searches. Target
                        // capacity below the shared allowance must remain observable.
                        foreach (int filler in new[] { 0, 2, 0 }) {
                            Assert.That(counter.Evaluate(hand, [0, 0, available, filler]), Is.EqualTo(expected));
                        }
                        // Evaluate returns a borrowed buffer; changing it must not
                        // mutate the request cache's independently owned result.
                        bool[] borrowed = counter.Evaluate(hand, [0, 0, available, 0]);
                        Array.Fill(borrowed, false);
                        Assert.That(counter.Evaluate(hand, [0, 0, available, 0]), Is.EqualTo(expected));
                    }
                }
            }
        }
        Assert.That(budget.PeakCells, Is.LessThanOrEqualTo(CalculationStoragePolicy.Standard.CellLimit));
    }

    [Test]
    public void FullTerminalCacheContinuesEvaluatingWithoutExceedingStorage() {
        CategoryBase target = new("Target");
        List<Card> cards = [new([], id: "search", search: new(CardSelector.For(target), false)),
            new([target], id: "target"), .. Enumerable.Range(0, 6).Select(row => new Card([], 5, id: $"filler-{row}"))];
        WorkBudget budget = new(CalculationWorkPolicy.Unlimited);
        PhysicalSearchHandCounter counter = new(cards, [new([new(target, 1, 32)])], budget, memoize: true);
        int[] hand = new int[cards.Count];
        hand[0] = 1;
        for (int scenario = 0; scenario < 25_000; scenario++) {
            int remaining = scenario;
            for (int row = 2; row < hand.Length; row++) {
                hand[row] = remaining % 6;
                remaining /= 6;
            }
            Assert.That(counter.Evaluate(hand, [0, 1, 0, 0, 0, 0, 0, 0]), Is.EqualTo(new[] { true }));
        }
        Assert.That(budget.PeakCells, Is.InRange(CalculationStoragePolicy.Standard.CellLimit - 32,
            CalculationStoragePolicy.Standard.CellLimit));
        // The late unseen capacity-zero key still uses the exact uncached path.
        Assert.That(counter.Evaluate(hand, new int[cards.Count]), Is.EqualTo(new[] { false }));
        Assert.That(counter.Evaluate(hand, [0, 1, 0, 0, 0, 0, 0, 0]), Is.EqualTo(new[] { true }));
    }

    [Test]
    public void GrowingCompilationBoundPreservesEverySmallerPhysicalHandAndNormalizedRoute() {
        CategoryBase a = new("A");
        CategoryBase b = new("B");
        List<Card> cards = [new([a, b], 2, "Same name", id: "ab", externalCardId: 123),
            new([a], 2, "Same name", id: "a", externalCardId: 123), new([b], id: "b"), new([], id: "f")];
        ComboAlternativeGroup alternatives = new([
            ComboAlternative.For(new ComboCategory(b, 2, 0, RequirementMaximumMode.HandSize)),
            ComboAlternative.For(new ComboCard("f", 1, 1))
        ]);
        List<Combo> combos = [
            new([], "Empty conjunction"),
            new([new(a, 0, 0)], "Zero maximum"),
            new([new(a, 0, 2)], "Initially vacuous fixed maximum"),
            new([new(a, 3, 0, RequirementMaximumMode.HandSize)], "Initially impossible dynamic maximum"),
            new([new(a, 1, 6), new(b, 1, 6)], "Distinct overlapping roles"),
            new([ComboCategory.SameCard([a, b], 1, 0, RequirementMaximumMode.HandSize)], "Same-card conjunction"),
            new([new(a, 1, 6)], "Direct and category need separate copies", cards: [new("ab", 1, 6)]),
            new([], "Distinct entry IDs", cards: [new("ab", 1, 6), new("a", 0, 0)]),
            new([new(a, 1, 6), new(a, 2, 2)], "Repeated category normalization"),
            new([], "Repeated direct normalization", cards: [new("ab", 1, 6), new("ab", 2, 2)]),
            new([new(a, 1, 0, RequirementMaximumMode.HandSize)], "Repeated OR with shared conjunction",
                alternativeGroups: [alternatives, alternatives])
        ];
        SearchCountingDiagnostics diagnostics = new();
        WorkBudget budget = new(CalculationWorkPolicy.Unlimited) { SearchDiagnostics = diagnostics };
        PhysicalSearchHandCounter counter = new(cards, combos, budget);
        int[] labels = cards.SelectMany((card, row) => Enumerable.Repeat(row, card.Copies)).ToArray();
        int largest = -1;
        int compilations = 0;
        foreach (int size in new[] { 0, 2, 1, 4, 3, 0, 6, 2 }) {
            if (size > largest) {
                largest = size;
                compilations++;
            }
            for (int subset = 0; subset < 1 << labels.Length; subset++) {
                if (BitOperations.PopCount((uint)subset) != size) {
                    continue;
                }
                int[] counts = new int[cards.Count];
                List<Card> hand = [];
                for (int label = 0; label < labels.Length; label++) {
                    if ((subset & 1 << label) != 0) {
                        counts[labels[label]]++;
                        hand.Add(cards[labels[label]]);
                    }
                }
                bool[] expected = combos.Select(combo => SmallDeckOracle.MatchesHand(hand, combo)).ToArray();
                Assert.That(counter.Evaluate(counts, new int[cards.Count]), Is.EqualTo(expected),
                    $"size={size}, labeled subset={subset}");
            }
            Assert.That(diagnostics.Counts.GetValueOrDefault("primary/physical event compilations"),
                Is.EqualTo(compilations), "Only a newly larger hand requires a new predicate program.");
        }
        Assert.That(budget.PeakCells, Is.LessThanOrEqualTo(CalculationStoragePolicy.Standard.CellLimit));
    }

    [Test]
    public void NumericAndSameCardSelectorsKeepTheirMeaningBelowTheCompiledHandBound() {
        CategoryBase role = new("Role");
        CategoryBase monster = new("Monster", CategorySource.Metadata, "kind:monster");
        CategoryBase range = new NumericPropertyRange(NumericPropertyKind.Level, 3, 5).ToCategory();
        List<Card> cards = [
            new([monster, new("Level 3", CategorySource.Metadata, "level:3")], id: "level-3", externalCardId: 1),
            new([monster, new("Level 4", CategorySource.Metadata, "level:4"), role], id: "level-4", externalCardId: 2),
            new([monster, new("Level 6", CategorySource.Metadata, "level:6"), role], id: "level-6", externalCardId: 3),
            new([monster, new("Rank 4", CategorySource.Metadata, "rank:4")], id: "rank-4", externalCardId: 4)
        ];
        List<Combo> combos = [new([new(range, 1, 0, RequirementMaximumMode.HandSize)]), new([new(range, 0, 0)]),
            new([new(range, 1, 2)], cards: [new("level-3", 1, 4)]),
            new([ComboCategory.SameCard([range, role], 1, 4)])];
        PhysicalSearchHandCounter counter = new(cards, combos, new(CalculationWorkPolicy.Unlimited));
        foreach (int subset in Enumerable.Range(0, 16).Reverse()) {
            int[] hand = Enumerable.Range(0, cards.Count).Select(row => (subset >> row) & 1).ToArray();
            // Only labels 0 and 1 are Level 3..5. Label 1 alone also has Role;
            // the direct Level-3 requirement must use a different physical copy.
            bool[] expected = [hand[0] + hand[1] > 0, hand[0] + hand[1] == 0,
                hand[0] == 1 && hand[1] == 1, hand[1] == 1];
            Assert.That(counter.Evaluate(hand, new int[cards.Count]), Is.EqualTo(expected), "labeled subset=" + subset);
        }
    }

    [TestCase(false), TestCase(true)]
    public void MixedRepeatedDrawsPaymentsAndTerminalSearchMatchIndependentPhysicalOrders(bool hiddenLoss) {
        CategoryBase a = new("A");
        CategoryBase b = new("B");
        List<Card> cards = [new([], id: "d", drawCount: 2, postDrawDiscardCount: 1, postDrawSelector: CardSelector.For(b)),
            new([], 2, id: "e", drawCount: 1, oncePerTurn: false, activationLimit: 2,
                preDrawRemovalCount: 1, preDrawSelector: CardSelector.For(a), preDrawDeckRemovalCount: hiddenLoss ? 1 : 0),
            new([], id: "s", search: new(CardSelector.AnyOfSelectors([CardSelector.For(a), CardSelector.For(b)]), false)),
            new([a, b], id: "ab"), new([a], id: "a"), new([b], id: "b")];
        List<Combo> combos = [new([new(a, 1, 3), new(b, 1, 3)], groupId: "g"),
            new([new(a, 0, 0), new(b, 1, 3)], groupId: "g"),
            new([], cards: [new("e", 1, 0, RequirementMaximumMode.HandSize), new("s", 0, 0)]),
            new([new(a, 1, 3)], cards: [new("ab", 1, 3)]), new([], "Completed branch", groupId: "complete")];
        List<ComboGroup> groups = [new("g", "Union"), new("complete", "Complete"), new("empty", "Empty")];
        DrawActivationPriority priority = new(DrawActivationMode.CustomPriority, ["e", "s", "d"]);
        TwoPhaseSearchOracle.Rational[] oracle = TwoPhaseSearchOracle.Count(cards, 2, combos, groups, priority);
        // The empty conjunction records every completed branch. Filtered mandatory
        // discards can fail, and those labels must remain in the original sample space.
        Assert.That(oracle[5].Numerator, Is.GreaterThan(BigInteger.Zero));
        Assert.That(oracle[5].Numerator, Is.LessThan(oracle[5].Denominator));
        foreach (PhysicalEnumerationMethod method in new[] { PhysicalEnumerationMethod.BatchStreaming, PhysicalEnumerationMethod.Frontier }) {
            WorkBudget budget = new(CalculationWorkPolicy.Unlimited);
            PostDrawCalculation calculation = new(cards, 2, budget, priority, deferIdentities: false);
            ExactCalculationResult actual = calculation.CalculateSearch(combos, groups, false, new(cards, budget),
                physical: true, method: method, coalesceOrdinary: false);
            ExactProbability[] rows = [actual.Total, .. actual.Combos, .. actual.Groups];
            Assert.That(rows, Has.Length.EqualTo(oracle.Length));
            for (int row = 0; row < rows.Length; row++) {
                Assert.That(rows[row].Numerator * oracle[row].Denominator,
                    Is.EqualTo(oracle[row].Numerator * rows[row].Denominator), method + "/row=" + row);
            }
        }
    }
}
