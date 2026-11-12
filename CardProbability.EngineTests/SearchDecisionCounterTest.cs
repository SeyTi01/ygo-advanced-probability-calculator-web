using System.Numerics;
using CardProbability.Internal.Model;
using CardProbability.Internal.Contracts;

using CardProbability.Internal.Compilation;
using CardProbability.Internal.Counting;
using CardProbability.Internal.Resources;
using CardProbability.Internal.Search;


namespace CardProbability.EngineTests;

[TestFixture]
public sealed class SearchDecisionCounterTest {
    private sealed record LabeledBound(HashSet<int> Rows, int Min, int Max);

    [Test]
    public void EvictedApplyEntriesAndSharedRootsPreserveWideMaskPhysicalCounts() {
        Random random = new(71813);
        List<Card> deck = Enumerable.Range(0, 70).Select(row => MakeCard(row, row % 10 == 0 || row == 69 ? 1 : 0)).ToList();
        int[] physical = Enumerable.Range(0, deck.Count).Where(row => deck[row].Copies > 0).ToArray();
        for (int hand = 0; hand <= physical.Length; hand++) {
            SearchCountingDiagnostics diagnostics = new();
            WorkBudget budget = new(CalculationWorkPolicy.Interactive) { SearchDiagnostics = diagnostics };
            SearchDecisionCounter evicting = new(deck, hand, budget, applyCapacity: 1);
            SearchDecisionCounter reference = new(deck, hand, new WorkBudget(CalculationWorkPolicy.Interactive));
            List<LabeledBound[]?> events = [null, [new([69], 2, 2)]];
            for (int index = 0; index < 18; index++) {
                events.Add(Enumerable.Range(0, 3).Select(_ => {
                    HashSet<int> eligible = physical.Where(_ => random.Next(2) == 0).ToHashSet();
                    int minimum = random.Next(3);
                    return new LabeledBound(eligible, minimum, random.Next(minimum, 5));
                }).ToArray());
            }
            events.Add(events[2]);
            foreach (List<LabeledBound[]?> subset in new[] { events.Take(8).ToList(), events, events.Skip(8).ToList(), events }) {
                List<CompiledEvent?> compiled = subset.Select(Compile).ToList();
                int root = evicting.Union(compiled);
                Assert.That(evicting.Count(root), Is.EqualTo(CountPhysical(physical, hand, subset)));
                Assert.That(evicting.Count(root), Is.EqualTo(reference.Count(reference.Union(compiled))));
            }
            Assert.That(evicting.Count(evicting.Union([new([])])), Is.EqualTo(reference.Count(1)));
            if (hand > 0) {
                Assert.That(diagnostics.Counts.GetValueOrDefault("primary/apply clears"), Is.GreaterThan(0));
            }
        }
    }

    [Test]
    public void SeededPredicateUnionsMatchLabeledHandsAtEverySize() {
        Random random = new(2042026);
        for (int trial = 0; trial < 80; trial++) {
            List<Card> deck = Enumerable.Range(0, 5).Select(row => MakeCard(row, random.Next(3))).ToList();
            int[] physical = deck.SelectMany((card, row) => Enumerable.Repeat(row, card.Copies)).ToArray();
            for (int size = 0; size <= physical.Length; size++) {
                List<LabeledBound[]?> predicates = Enumerable.Range(0, random.Next(1, 9)).Select(_ => (LabeledBound[]?)
                    Enumerable.Range(0, random.Next(5)).Select(_ => {
                        int min = random.Next(size + 2);
                        HashSet<int> eligible = Enumerable.Range(0, 5).Where(_ => random.Next(2) == 0).ToHashSet();
                        return new LabeledBound(eligible, min, random.Next(min, size + 3));
                    }).ToArray()).ToList();
                predicates.Add(null);
                WorkBudget budget = new(CalculationWorkPolicy.Interactive);
                SearchDecisionCounter counter = new(deck, size, budget);
                BigInteger actual = counter.Count(counter.Union(predicates.Select(Compile).ToList()));
                Assert.That(actual, Is.EqualTo(CountPhysical(physical, size, predicates)), $"trial {trial}, size {size}");
            }
        }
    }

    private static CompiledEvent? Compile(LabeledBound[]? bounds) => bounds is null ? null : new(bounds.Select(bound =>
        new CountBound(bound.Rows.Aggregate(BigInteger.Zero, (mask, row) => mask | (BigInteger.One << row)), bound.Min, bound.Max)).ToArray());

    private static BigInteger CountPhysical(int[] physical, int size, IEnumerable<LabeledBound[]?> predicates) {
        BigInteger count = 0;
        for (int selection = 0; selection < 1 << physical.Length; selection++) {
            if (BitOperations.PopCount((uint)selection) != size) {
                continue;
            }
            if (predicates.Where(predicate => predicate is not null).Any(predicate => predicate!.All(bound => {
                int matches = physical.Where((row, copy) => (selection & (1 << copy)) != 0 && bound.Rows.Contains(row)).Count();
                return matches >= bound.Min && matches <= bound.Max;
            }))) {
                count++;
            }
        }
        return count;
    }
    [Test]
    public void LargeMultiplicitiesUseExactIntegerWeightsAndChargeTheirPayload() {
        List<Card> deck = [MakeCard(0, 100), MakeCard(1, 100)];
        WorkBudget budget = new(CalculationWorkPolicy.Interactive);
        SearchDecisionCounter counter = new(deck, 100, budget);
        int root = counter.Union([new([new(1, 50, 50)])]);
        BigInteger choose = 1;
        for (int i = 1; i <= 50; i++) {
            choose = choose * (101 - i) / i;
        }
        Assert.That(counter.Count(root), Is.EqualTo(choose * choose));
        Assert.That(budget.PeakCells, Is.GreaterThan(WorkBudget.IntegerCells(choose * choose)));
        long spent = budget.Spent;
        WorkBudget insufficient = new(new(spent - 1));
        SearchDecisionCounter limited = new(deck, 100, insufficient);
        Assert.Throws<CardProbability.Internal.Contracts.ProbabilityCalculationLimitException>(() => limited.Count(limited.Union([new([new(1, 50, 50)])])));
    }

    [Test]
    public void DecisionConstructionHonorsStandardStorageBoundary() {
        List<Card> deck = Enumerable.Range(0, 20).Select(row => MakeCard(row, 3)).ToList();
        Random random = new(713);
        List<CompiledEvent?> events = Enumerable.Range(0, 200).Select(_ => (CompiledEvent?)new CompiledEvent(
            Enumerable.Range(0, 6).Select(_ => new CountBound(random.Next(1 << 20), 2, 5)).ToArray())).ToList();
        WorkBudget budget = new(CalculationWorkPolicy.Interactive);
        SearchDecisionCounter counter = new(deck, 10, budget);
        CardProbability.Internal.Contracts.ProbabilityCalculationLimitException? error = Assert.Throws<CardProbability.Internal.Contracts.ProbabilityCalculationLimitException>(() => counter.Union(events));
        Assert.That(error!.Reason, Is.EqualTo(CardProbability.Internal.Contracts.ProbabilityCalculationLimitReason.Storage));
        Assert.That(budget.PeakEntries, Is.LessThanOrEqualTo(CalculationStoragePolicy.Standard.EntryLimit + 1));
    }
    private static Card MakeCard(int row, int copies) => new($"r{row}", copies, [], new());
}
