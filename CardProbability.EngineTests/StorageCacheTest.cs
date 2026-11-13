using System.Numerics;
using CardProbability.Internal.Compilation;
using CardProbability.Internal.Contracts;
using CardProbability.Internal.Model;
using CardProbability.Internal.Resources;
using CardProbability.Internal.Search;

namespace CardProbability.EngineTests;

[TestFixture]
public sealed class StorageCacheTest {
    private sealed record LabeledBound(bool[] Rows, int Minimum, int Maximum);

    [TestCaseSource(nameof(Policies))]
    public void ReusedPredicateRootsMatchLabeledPhysicalHands(long entries, long cells, bool evictApply) {
        const int copies = 10;
        const int handSize = 4;
        List<Card> deck = Enumerable.Range(0, copies).Select(row => new Card("r" + row, 1, [], new())).ToList();
        int[][] hands = Enumerable.Range(0, 1 << copies)
            .Where(selection => BitOperations.PopCount((uint)selection) == handSize)
            .Select(selection => Enumerable.Range(0, copies).Where(copy => (selection & (1 << copy)) != 0).ToArray())
            .ToArray();
        // Numeric policies exercise the same boundary used by public callers;
        // custom policies intentionally have no High/VeryHigh tier identity.
        SearchCountingDiagnostics diagnostics = new();
        WorkBudget budget = new(CalculationWorkPolicy.Unlimited, new(entries, cells)) { SearchDiagnostics = diagnostics };
        SearchDecisionCounter counter = new(deck, handSize, budget, applyCapacity: evictApply ? 1 : null);
        Random random = new(2832026);
        List<(int Root, LabeledBound[][] Events, BigInteger Expected)> roots = [];
        for (int index = 0; index < 144; index++) {
            LabeledBound[][] events = Enumerable.Range(0, 2).Select(_ => Enumerable.Range(0, 2).Select(_ => {
                bool[] rows = Enumerable.Range(0, copies).Select(_ => random.Next(2) == 0).ToArray();
                int minimum = random.Next(2);
                return new LabeledBound(rows, minimum, random.Next(minimum, 4));
            }).ToArray()).ToArray();
            int root = counter.Union(events.Select(Compile));
            BigInteger expected = hands.Count(hand => Matches(hand, events));
            roots.Add((root, events, expected));
            Assert.That(counter.Count(root), Is.EqualTo(expected), "initial root " + index);
        }

        // Later roots can displace memoized earlier work. Revisit old roots and
        // overlap their success sets, as total/group rows do, without summing
        // the probabilities of successful search assignments.
        for (int index = roots.Count - 1; index >= 0; index--) {
            Assert.That(counter.Count(roots[index].Root), Is.EqualTo(roots[index].Expected), "revisited root " + index);
            if (index % 12 == 0) {
                (int Root, LabeledBound[][] Events, BigInteger Expected) first = roots[index];
                (int Root, LabeledBound[][] Events, BigInteger Expected) second = roots[(index + 37) % roots.Count];
                BigInteger expected = hands.Count(hand => Matches(hand, first.Events) || Matches(hand, second.Events));
                Assert.That(counter.Count(counter.Or(first.Root, second.Root)), Is.EqualTo(expected), "overlapping roots " + index);
            }
        }

        Assert.Multiple(() => {
            Assert.That(budget.PeakEntries, Is.LessThanOrEqualTo(entries));
            Assert.That(budget.PeakCells, Is.LessThanOrEqualTo(cells));
        });
        TestContext.Out.WriteLine($"count misses={diagnostics.Counts.GetValueOrDefault("primary/count miss unique")}; "
            + $"saturated visits={diagnostics.Counts.GetValueOrDefault("primary/count saturated visits")}; "
            + $"apply clears={diagnostics.Counts.GetValueOrDefault("primary/apply clears")}");
    }

    [Test]
    public void ReusingAnAlreadyCountedRootStillObservesCancellation() {
        using CancellationTokenSource cancellation = new();
        CalculationStoragePolicy high = CalculationStoragePolicy.High;
        WorkBudget budget = new(CalculationWorkPolicy.Unlimited, new(high.EntryLimit, high.CellLimit), cancellation.Token);
        List<Card> deck = Enumerable.Range(0, 8).Select(row => new Card("r" + row, 1, [], new())).ToList();
        SearchDecisionCounter counter = new(deck, 3, budget);
        int root = counter.Union([new([new(15, 1, 2)])]);
        // Choose one or two of the first four labeled copies and fill the hand
        // from the other four: C(4,1)C(4,2) + C(4,2)C(4,1).
        Assert.That(counter.Count(root), Is.EqualTo(new BigInteger(48)));
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => counter.Count(root));
    }

    private static bool Matches(int[] hand, LabeledBound[][] events) => events.Any(bounds => bounds.All(bound => {
        int count = hand.Count(copy => bound.Rows[copy]);
        return count >= bound.Minimum && count <= bound.Maximum;
    }));

    private static CompiledEvent Compile(LabeledBound[] bounds) => new(bounds.Select(bound => {
        BigInteger mask = 0;
        for (int row = 0; row < bound.Rows.Length; row++) {
            if (bound.Rows[row]) {
                mask |= BigInteger.One << row;
            }
        }
        return new CountBound(mask, bound.Minimum, bound.Maximum);
    }).ToArray());

    private static IEnumerable<TestCaseData> Policies() {
        CalculationStoragePolicy standard = CalculationStoragePolicy.Standard;
        CalculationStoragePolicy high = CalculationStoragePolicy.High;
        CalculationStoragePolicy veryHigh = CalculationStoragePolicy.VeryHigh;
        (string Name, long Entries, long Cells)[] policies = [
            ("Standard", standard.EntryLimit, standard.CellLimit),
            ("High", high.EntryLimit, high.CellLimit),
            ("VeryHigh", veryHigh.EntryLimit, veryHigh.CellLimit),
            ("ExtraEntriesOnly", high.EntryLimit, standard.CellLimit),
            ("ExtraCellsOnly", standard.EntryLimit, high.CellLimit)
        ];
        foreach ((string name, long entries, long cells) in policies) {
            foreach (bool evict in new[] { false, true }) {
                yield return new TestCaseData(entries, cells, evict).SetName("{m}(" + name + (evict ? ",eviction)" : ",default)"));
            }
        }
    }
}
