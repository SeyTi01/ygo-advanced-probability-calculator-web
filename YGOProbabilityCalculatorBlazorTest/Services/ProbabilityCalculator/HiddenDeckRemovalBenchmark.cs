using System.Diagnostics;
using System.Text.Json;
using Microsoft.JSInterop;
using Moq;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.Session;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

public partial class DrawEffectsBenchmark {
    [Test]
    public async Task ExportHiddenDeckRemovalCases() {
        CategoryBase starter = new("Starter");
        CategoryBase extender = new("Extender");
        List<Combo> combos = [new([new(starter, 1, 0, RequirementMaximumMode.HandSize), new(extender, 1, 0, RequirementMaximumMode.HandSize)], groupId: "g"),
            new([ComboCategory.SameCard([starter, extender], 1, 0, RequirementMaximumMode.HandSize)])];
        List<object> output = [];
        foreach (int size in new[] { 40, 60 }) {
            foreach (int hand in new[] { 5, 6 }) {
                foreach (string name in new[] { "control", "one-pot", "pot-simple", "three-pot", "graceful" }) {
                    List<Card> effects = name switch {
                        "control" => [],
                        "one-pot" => [Pot(1)],
                        "three-pot" => [Pot(3)],
                        "pot-simple" => [Pot(3), new([], 3, id: "upstart", drawCount: 1, drawOncePerTurn: false), new([], 3, id: "greed", drawCount: 2, drawOncePerTurn: false)],
                        _ => [new([], 3, id: "graceful", drawCount: 3, drawOncePerTurn: false, postDrawDiscardCount: 2)]
                    };
                    List<Card> deck = [new([starter], 6, id: "a"), new([extender], 6, id: "b"), new([starter, extender], 3, id: "ab"),
                        new([], size - 15 - effects.Sum(card => card.Copies), id: "filler"), .. effects];
                    Measure($"{size}-{hand}-{name}", deck, combos, hand, CalculationStorageTier.Standard);
                }
            }
        }
        SessionService codec = new(Mock.Of<IJSRuntime>(), new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());
        SessionState example = await codec.LoadSessionAsync(File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "example_session_state.json")));
        List<Card> complex = example.Cards.ToList();
        int index = complex.FindIndex(card => card.Name == "Mulcharmy Fuwalos");
        complex[index] = Pot(complex[index].Copies);
        int other = complex.FindIndex(card => card.Name == "Ash Blossom & Joyous Spring");
        if (other < 0) {
            other = index == 0 ? 1 : 0;
        }
        CategoryBase dark = new("DARK", CategorySource.Metadata, "attribute:dark");
        complex[other] = new([], complex[other].Copies, id: "allure", drawCount: 2, drawOncePerTurn: false,
            postDrawDiscardCount: 1, postDrawSelector: CardSelector.For(dark));
        foreach (CalculationStorageTier tier in new[] { CalculationStorageTier.Standard, CalculationStorageTier.VeryHigh }) {
            Measure($"vs-k9-pot-allure-{tier}", CategoryMembership.Materialize(complex, example.Categories).ToList(), example.Combos, 5, tier);
        }
        string directory = Environment.GetEnvironmentVariable("YGO_DRAW_BENCHMARK_OUTPUT") ?? throw new InvalidOperationException("Set YGO_DRAW_BENCHMARK_OUTPUT.");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "hidden-deck-worker-cases.json"), JsonSerializer.Serialize(output));

        static Card Pot(int copies) => new Card([], copies, id: "pot", drawCount: 2).WithPreDrawDeckRemovalCount(10);

        void Measure(string name, List<Card> deck, List<Combo> routes, int hand, CalculationStorageTier tier) {
            List<ComboGroup> groups = [new("g", "Routes")];
            List<double> timings = [];
            List<long> allocations = [];
            long work = 0;
            string outcome = "Success";
            ExactCalculationResult? exact = null;
            ProbabilityCalculationResult? reference = null;
            long peakCells = 0;
            int peakStates = 0;
            for (int trial = 0; trial < 4; trial++) {
                WorkBudget budget = new(CalculationWorkPolicy.Interactive, CalculationStoragePolicy.ForTier(tier));
                PostDrawCalculation? post = deck.Any(card => card.PreDrawRemovalCount > 0 || card.PostDrawDiscardCount > 0)
                    ? new(deck, hand, budget) : null;
                long allocated = GC.GetAllocatedBytesForCurrentThread();
                Stopwatch timer = Stopwatch.StartNew();
                try {
                    exact = post is null ? new DrawEffectCalculation(deck, hand, budget).Calculate(routes, groups)
                        : post.Calculate(routes, groups);
                    reference = exact.ToPublic(routes, groups, budget);
                }
                catch (ProbabilityCalculationLimitException exception) {
                    outcome = exception.Reason.ToString();
                }
                timer.Stop();
                allocations.Add(GC.GetAllocatedBytesForCurrentThread() - allocated);
                timings.Add(timer.Elapsed.TotalMilliseconds);
                work = budget.Spent;
                peakCells = post?.PeakCells ?? 0;
                peakStates = post?.PeakStates ?? 0;
                if (outcome != "Success") {
                    break;
                }
            }
            CalculationSnapshot snapshot = CalculationSnapshot.Capture(deck, routes, hand, groups, CalculationWorkPolicy.Interactive,
                storagePolicy: CalculationStoragePolicy.ForTier(tier), options: new());
            output.Add(new { Name = name, Outcome = outcome, Work = work, PeakCells = peakCells, PeakStates = peakStates,
                Milliseconds = timings, AllocatedBytes = allocations, Path = DrawExecutionAssessment.Assess(deck, routes, hand, true).Path.ToString(),
                Probability = reference?.TotalProbability, ReferenceResults = reference,
                ExactResults = exact is null ? null : new[] { exact.Total }.Concat(exact.Combos).Concat(exact.Groups).Select(value => $"{value.Numerator}/{value.Denominator}").ToArray(),
                Input = JsonSerializer.Deserialize<JsonElement>(snapshot.Json) });
            TestContext.Out.WriteLine($"{name}: {outcome}; work={work}; peakCells={peakCells}; ms={string.Join(',', timings)}; allocated={string.Join(',', allocations)}");
        }
    }
}
