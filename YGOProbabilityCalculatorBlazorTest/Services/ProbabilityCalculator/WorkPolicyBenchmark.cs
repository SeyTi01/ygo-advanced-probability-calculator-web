using System.Diagnostics;
using System.Runtime.InteropServices;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

// Opt in and externally bound the test process; never assert elapsed time.
[TestFixture, Explicit("Bounded work-policy measurements")]
public class WorkPolicyBenchmark {
    [Test]
    public void MeasureRestrictiveRoutes() {
        TestContext.Out.WriteLine($"{RuntimeInformation.FrameworkDescription}; {RuntimeInformation.OSDescription}; {RuntimeInformation.ProcessArchitecture}; CPUs={Environment.ProcessorCount}");
        foreach (var (count, hand) in new[] { (18, 5), (20, 5), (21, 5), (18, 6), (16, 7), (15, 9), (15, 15), (15, 30), (22, 5) }) {
            var categories = Enumerable.Range(0, count).Select(i => new CategoryBase($"Role{i}")).ToArray();
            var deck = categories.Select((c, i) => new Card([c], 2, id: $"c{i}")).Append(new Card([], 60 - count * 2, id: "blank")).ToList();
            var combos = categories.Select((c, i) => new Combo([new(c, 1, 1)], groupId: $"g{i % 2}")).ToList();
            List<ComboGroup> groups = [new("g0", "Even"), new("g1", "Odd")];
            var service = new ProbabilityCalculatorService();
            foreach (var units in new[] { CalculationWorkPolicy.Default.WorkUnits, CalculationWorkPolicy.Interactive.WorkUnits }) {
                var before = GC.GetAllocatedBytesForCurrentThread();
                var timer = Stopwatch.StartNew();
                string outcome;
                try {
                    var result = units == CalculationWorkPolicy.Default.WorkUnits
                        ? service.CalculateProbabilityResults(deck, combos, hand, groups)
                        : service.CalculateProbabilityResults(deck, combos, hand, groups, new(units));
                    // Independently count the complement: draws from each two-copy
                    // role must be zero or two, with the remaining draws all blank.
                    var denominator = Choose(60, hand);
                    long failed = 0;
                    for (var pairs = 0; pairs <= Math.Min(count, hand / 2); pairs++)
                        failed += Choose(count, pairs) * Choose(60 - count * 2, hand - 2 * pairs);
                    Assert.That(result.TotalProbability, Is.EqualTo(1d - (double)failed / denominator).Within(1e-12));
                    outcome = $"success p={result.TotalProbability:R}";
                } catch (ProbabilityCalculationLimitException ex) { outcome = ex.Reason.ToString(); }
                timer.Stop();
                TestContext.Out.WriteLine($"routes={count}; hand={hand}; deck=60; units={units}; ms={timer.Elapsed.TotalMilliseconds:F2}; allocatedBytes={GC.GetAllocatedBytesForCurrentThread() - before}; {outcome}");
            }
            if ((count, hand) is (20, 5) or (18, 6) or (15, 15)) {
                long low = 0, high = 100_000_000;
                while (high - low > 100_000) {
                    var midpoint = (low + high) / 2;
                    try {
                        service.CalculateProbabilityResults(deck, combos, hand, groups, new(midpoint));
                        high = midpoint;
                    } catch (ProbabilityCalculationLimitException ex) when (ex.Reason == ProbabilityCalculationLimitReason.Work) { low = midpoint; }
                }
                TestContext.Out.WriteLine($"routes={count}; hand={hand}; chargedWork=({low},{high}]; processWorkingSetBytes={Process.GetCurrentProcess().WorkingSet64}; managedHeapBytes={GC.GetTotalMemory(false)}");
            }
            if ((count, hand) == (20, 5)) {
                var error = Assert.Throws<ProbabilityCalculationLimitException>(() => service.CalculateProbabilityResults(deck, combos, hand, groups));
                Assert.That(error!.Reason, Is.EqualTo(ProbabilityCalculationLimitReason.Work), "large policies must not leak into omitted defaults");
            }
        }
    }

    private static long Choose(int n, int k) {
        if (k > n) return 0;
        System.Numerics.BigInteger result = 1;
        for (var i = 1; i <= k; i++) result = result * (n - i + 1) / i;
        return (long)result;
    }

    [Test]
    public void RedundantExclusionsStillHaveABoundedWorkCutoff() {
        var categories = Enumerable.Range(0, 15).Select(i => new CategoryBase($"Excluded{i}")).ToArray();
        var deck = categories.SelectMany((c, i) => Enumerable.Range(0, 4).Select(j => new Card([c], id: $"c{i}-{j}"))).ToList();
        var combos = categories.Select(c => new Combo([new(c, 0, 0)])).ToList();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var timer = Stopwatch.StartNew();
        var error = Assert.Throws<ProbabilityCalculationLimitException>(() =>
            new ProbabilityCalculatorService().CalculateProbabilityResults(deck, combos, 9, null, CalculationWorkPolicy.Interactive));
        Assert.That(error!.Reason, Is.EqualTo(ProbabilityCalculationLimitReason.Work));
        TestContext.Out.WriteLine($"redundant-exclusions: rows=60; routes=15; hand=9; units={CalculationWorkPolicy.Interactive.WorkUnits}; ms={timer.Elapsed.TotalMilliseconds:F2}; allocatedBytes={GC.GetAllocatedBytesForCurrentThread() - before}; {error.Reason}");
    }
}
