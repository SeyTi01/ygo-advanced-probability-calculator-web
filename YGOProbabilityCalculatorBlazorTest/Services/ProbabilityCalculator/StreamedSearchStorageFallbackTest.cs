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
public sealed class StreamedSearchStorageFallbackTest {
    [Test]
    public void StorageFallbackDiscardsPartialAbsentRowsAndRetainsTheirWork() {
        CategoryBase[] roles = [new("A"), new("B"), new("C")];
        List<Card> cards = [new([], 2, id: "draw", drawCount: 2, postDrawDiscardCount: 1),
            new([], id: "search-a", search: new(CardSelector.For(roles[0]), false)),
            new([], id: "search-b", search: new(CardSelector.For(roles[1]), false))];
        for (int mask = 1; mask <= 7; mask++) {
            int current = mask;
            cards.Add(new(roles.Where((_, index) => (current & (1 << index)) != 0), 2, id: "target-" + mask));
        }
        cards.Add(new([], 4, id: "inert"));
        List<Combo> combos = [
            new([new(roles[0], 1, 2), new(roles[1], 1, 2)], groupId: "positive"),
            new([new(roles[1], 1, 2), new(roles[2], 1, 2)], groupId: "positive"),
            new([new(roles[0], 1, 2), new(roles[2], 1, 2)], groupId: "positive"),
            new([new(roles[0], 0, 0)], groupId: "exclusions"),
            new([new(roles[1], 0, 0)], groupId: "exclusions"),
            new([], cards: [new("target-1", 1, 1), new("draw", 0, 0)], groupId: "exclusions")
        ];
        List<ComboGroup> groups = [new("positive", "Positive"), new("exclusions", "Exclusions"), new("empty", "Empty")];
        WorkBudget referenceBudget = new(CalculationWorkPolicy.Unlimited);
        ExactCalculationResult[] reference = Calculate(referenceBudget, observe: true);
        // The observed route streams both partitions. Its measured retained
        // entry requirement leaves no room for the larger analytic counting
        // graph, but still permits that same stream after analytic failure.
        CalculationStoragePolicy policy = Storage(referenceBudget.PeakEntries, CalculationStoragePolicy.Standard.CellLimit);
        SearchCountingDiagnostics diagnostics = new();
        WorkBudget budget = new(CalculationWorkPolicy.Unlimited, policy) { SearchDiagnostics = diagnostics };
        ExactCalculationResult[] actual = Calculate(budget, observe: false);
        Assert.That(diagnostics.Counts.GetValueOrDefault("primary/streamed absent storage fallback"), Is.EqualTo(1));
        Assert.That(diagnostics.Counts.GetValueOrDefault("primary/streamed absent counted rows"), Is.GreaterThan(0),
            "The failed transaction must already have written an absent result row.");
        for (int pass = 0; pass < reference.Length; pass++) {
            Assert.That(Rows(actual[pass]), Is.EqualTo(Rows(reference[pass])), "Primary/witness/known-loss/scalar-loss pass " + pass);
        }
        Assert.That(budget.Spent, Is.GreaterThan(referenceBudget.Spent), "The abandoned count remains charged before replay.");
        Assert.That(budget.PeakEntries, Is.GreaterThan(policy.EntryLimit),
            "PeakEntries includes the rejected request; it is not a retained-memory measurement.");

        ExactCalculationResult[] Calculate(WorkBudget calculationBudget, bool observe) {
            PostDrawCalculation calculation = new(cards, 2, calculationBudget, deferIdentities: false) {
                ObserveTerminal = observe ? (_, _) => { } : null
            };
            return [calculation.CalculateSearch(combos, groups, false, new(cards, calculationBudget)),
                calculation.CalculateSearchLower(combos, groups, true),
                calculation.CalculateSearchLower(combos, groups, false),
                calculation.CalculateSearchLower(combos, groups, false, useKnownLoss: false)];
        }
    }

    private static CalculationStoragePolicy Storage(long entries, long cells) {
        ConstructorInfo constructor = typeof(CalculationStoragePolicy).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
            null, [typeof(CalculationStorageTier), typeof(long), typeof(long)], null)!;
        return (CalculationStoragePolicy)constructor.Invoke([CalculationStorageTier.Standard, entries, cells]);
    }

    private static ProbabilityFraction[] Rows(ExactCalculationResult result) =>
        new[] { result.Total }.Concat(result.Combos).Concat(result.Groups).Select(value => value.ToFraction()).ToArray();
}
