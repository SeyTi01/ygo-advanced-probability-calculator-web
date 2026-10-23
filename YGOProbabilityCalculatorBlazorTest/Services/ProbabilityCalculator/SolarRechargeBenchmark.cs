using System.Diagnostics;
using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.DrawResolution;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture, Explicit("Native and optimized worker benchmark export")]
public class SolarRechargeBenchmark {
    [Test]
    public void Export() {
        CategoryBase starter = new("Starter");
        CategoryBase extender = new("Extender");
        CategoryBase lightsworn = new("Lightsworn", CategorySource.Metadata, "archetype:lightsworn");
        CategoryBase monster = new("Monster", CategorySource.Metadata, "kind:monster");
        List<Combo> combos = [new([new(starter, 1, 0, RequirementMaximumMode.HandSize), new(extender, 1, 0, RequirementMaximumMode.HandSize)], groupId: "g"),
            new([ComboCategory.SameCard([starter, extender], 1, 0, RequirementMaximumMode.HandSize)], groupId: "g")];
        List<ComboGroup> groups = [new("g", "Routes")];
        List<object> output = [];
        foreach ((int size, int hand) in new[] { (40, 5), (60, 6) }) {
            foreach (string name in new[] { "desires", "graceful", "solar" }) {
                Card effect = name switch {
                    "desires" => new([], id: "effect", drawCount: 2, preDrawDeckRemovalCount: 10),
                    "graceful" => new([], 3, id: "effect", drawCount: 3, drawOncePerTurn: false, postDrawDiscardCount: 2),
                    _ => YGOProbabilityCalculatorBlazor.Services.CardEffects.CardEffectRegistry.Default.RestoreBuiltIn(
                        new([], 3, id: "effect", externalCardId: 691925))
                };
                List<Card> deck = [new([starter, lightsworn, monster], 6, id: "a"), new([extender], 6, id: "b"),
                    new([starter, extender, lightsworn, monster], 3, id: "ab"), new([], size - 15 - effect.Copies, id: "filler"), effect];
                List<double> timings = [];
                List<long> allocations = [];
                long work = 0;
                long peakCells = 0;
                int peakStates = 0;
                string outcome = "Success";
                ExactCalculationResult? exact = null;
                ProbabilityCalculationResult? result = null;
                for (int trial = -1; trial < 3; trial++) {
                    WorkBudget budget = new(CalculationWorkPolicy.Interactive);
                    PostDrawCalculation? post = effect.EffectPlan.RequiresHandResolution ? new(deck, hand, budget) : null;
                    long start = GC.GetAllocatedBytesForCurrentThread();
                    Stopwatch timer = Stopwatch.StartNew();
                    try {
                        exact = post is null ? new DrawEffectCalculation(deck, hand, budget).Calculate(combos, groups) : post.Calculate(combos, groups);
                        result = exact.ToPublic(combos, groups, budget);
                    }
                    catch (ProbabilityCalculationLimitException error) {
                        outcome = error.Reason.ToString();
                    }
                    timer.Stop();
                    if (trial >= 0) {
                        timings.Add(timer.Elapsed.TotalMilliseconds);
                        allocations.Add(GC.GetAllocatedBytesForCurrentThread() - start);
                    }
                    work = budget.Spent;
                    peakStates = post?.PeakStates ?? 0;
                    peakCells = post?.PeakCells ?? 0;
                }
                CalculationSnapshot snapshot = CalculationSnapshot.Capture(deck, combos, hand, groups, options: new());
                output.Add(new { Name = $"{size}-{hand}-{name}", Outcome = outcome, Work = work, PeakStates = peakStates, PeakCells = peakCells,
                    Milliseconds = timings, AllocatedBytes = allocations, Path = DrawExecutionAssessment.Assess(deck, combos, hand, true).Path.ToString(),
                    Probability = result?.TotalProbability, ReferenceResults = result,
                    ExactResults = exact is null ? null : new[] { exact.Total }.Concat(exact.Combos).Concat(exact.Groups).Select(value => $"{value.Numerator}/{value.Denominator}").ToArray(),
                    Input = JsonSerializer.Deserialize<JsonElement>(snapshot.Json) });
                TestContext.Out.WriteLine($"{size}-{hand}-{name}: {outcome}, work={work}, cells={peakCells}, ms={string.Join(',', timings)}");
            }
        }
        File.WriteAllText(Environment.GetEnvironmentVariable("YGO_SOLAR_BENCHMARK_OUTPUT")
            ?? throw new InvalidOperationException("Set YGO_SOLAR_BENCHMARK_OUTPUT."), JsonSerializer.Serialize(output));
    }
}
