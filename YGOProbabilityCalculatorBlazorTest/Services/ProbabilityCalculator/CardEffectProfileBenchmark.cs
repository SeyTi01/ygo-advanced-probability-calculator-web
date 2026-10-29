using YGOProbabilityCalculatorBlazorTest.Services.CardEffects;
using System.Diagnostics;
using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.DeckImport;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.DrawResolution;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture, Explicit("Card profile parity and performance matrix"), NonParallelizable]
public class CardEffectProfileBenchmark {
    [Test]
    public void Measure() {
        CategoryBase starter = new("Starter");
        CategoryBase extender = new("Extender");
        CategoryBase dark = new("DARK", CategorySource.Metadata, "attribute:dark");
        CategoryBase level = new("Level 8", CategorySource.Metadata, "level:8");
        CategoryBase monster = new("Monster", CategorySource.Metadata, "kind:monster");
        CategoryBase orcust = new("Orcust", CategorySource.Metadata, "archetype:orcust");
        List<Combo> combos = [new([new(starter, 1, 5), new(extender, 1, 5)], groupId: "g"),
            new([ComboCategory.SameCard([starter, extender], 1, 5)], groupId: "g")];
        List<ComboGroup> groups = [new("g", "Routes"), new("empty", "Empty")];
        List<object> output = [];
        foreach (string name in new[] { "control", "desires", "desires-allure", "three-graceful", "trade-in", "return" }) {
            List<Card> effects = name switch {
                "control" => [],
                "desires" => [Effect(35261759, 3)],
                "desires-allure" => [Effect(35261759, 3), Effect(1475311, 2)],
                "three-graceful" => [Effect(79571449, 3)],
                "trade-in" => [Effect(38120068, 2)],
                _ => [Effect(26845680, 2)]
            };
            List<Card> cards = [new([starter, dark, level, monster, orcust], 6, id: "a"),
                new([extender], 6, id: "b"), new([starter, extender, dark, monster, orcust], 3, id: "ab"),
                new([], 25 - effects.Sum(card => card.Copies), id: "filler"), .. effects];
            List<double> times = [];
            List<long> allocations = [];
            List<double> captureTimes = [];
            List<long> captureAllocations = [];
            long work = 0;
            int peakStates = 0;
            long peakCells = 0;
            ExactCalculationResult? exact = null;
            ProbabilityCalculationResult? result = null;
            string outcome = "Success";
            CalculationSnapshot? snapshot = null;
            for (int trial = -1; trial < 3; trial++) {
                WorkBudget budget = new(CalculationWorkPolicy.Interactive);
                PostDrawCalculation? post = cards.Any(card => card.PreDrawRemovalCount > 0 || card.PostDrawDiscardCount > 0)
                    ? new(cards, 5, budget) : null;
                long start = GC.GetAllocatedBytesForCurrentThread();
                Stopwatch timer = Stopwatch.StartNew();
                try {
                    exact = post is null ? new DrawEffectCalculation(cards, 5, budget).Calculate(combos, groups)
                        : post.Calculate(combos, groups);
                    result = exact.ToPublic(combos, groups, budget);
                }
                catch (ProbabilityCalculationLimitException error) {
                    outcome = error.Reason.ToString();
                }
                timer.Stop();
                long allocated = GC.GetAllocatedBytesForCurrentThread() - start;
                if (trial >= 0) {
                    times.Add(timer.Elapsed.TotalMilliseconds);
                    allocations.Add(allocated);
                }
                start = GC.GetAllocatedBytesForCurrentThread();
                timer.Restart();
                snapshot = CalculationSnapshot.Capture(cards, combos, 5, groups, CalculationWorkPolicy.Interactive, options: new());
                timer.Stop();
                if (trial >= 0) {
                    captureTimes.Add(timer.Elapsed.TotalMilliseconds);
                    captureAllocations.Add(GC.GetAllocatedBytesForCurrentThread() - start);
                }
                work = budget.Spent;
                peakStates = post?.PeakStates ?? 0;
                peakCells = post?.PeakCells ?? 0;
            }
            if (outcome == "Success") {
                Assert.That(JsonSerializer.Serialize(CalculationWire.ReadResult(CalculationWire.Execute(snapshot!.Json))),
                    Is.EqualTo(JsonSerializer.Serialize(result)));
            }
            output.Add(new { Name = name, Outcome = outcome, Work = work, PeakStates = peakStates, PeakCells = peakCells,
                Milliseconds = times, AllocatedBytes = allocations, CaptureMilliseconds = captureTimes, CaptureAllocatedBytes = captureAllocations,
                Path = DrawExecutionAssessment.Assess(cards, combos, 5, true).Path.ToString(),
                Risk = DrawExecutionAssessment.Assess(cards, combos, 5, true).ElevatedRisk,
                Probability = result?.TotalProbability, ReferenceResults = result,
                ExactResults = exact is null ? null : new[] { exact.Total }.Concat(exact.Combos).Concat(exact.Groups)
                    .Select(value => $"{value.Numerator}/{value.Denominator}").ToArray(),
                Input = JsonSerializer.Deserialize<JsonElement>(snapshot!.Json) });
            TestContext.Out.WriteLine($"{name}: {outcome}, work={work}, states={peakStates}, cells={peakCells}, ms={string.Join(',', times)}");
        }
        File.WriteAllText(Environment.GetEnvironmentVariable("YGO_PROFILE_BENCHMARK_OUTPUT")
            ?? throw new InvalidOperationException("Set YGO_PROFILE_BENCHMARK_OUTPUT."), JsonSerializer.Serialize(output));
    }

    private static Card Effect(int id, int copies) => CuratedDrawEffectPresets.ApplyToSelectedCardIfPristine(
        new([], copies, id: id.ToString(), externalCardId: id), new() { Id = id });
}
