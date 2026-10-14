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

[TestFixture, Explicit("Selector performance and real worker fixtures")]
public class SameCardBenchmark {
    [Test]
    public void ExportSameCardWorkerCases() {
        CategoryBase a = SameCardSelectorTest.A;
        CategoryBase b = SameCardSelectorTest.B;
        CategoryBase c = SameCardSelectorTest.C;
        CategoryBase d = SameCardSelectorTest.D;
        List<Card> deck = [new([a, b, c], 6, "Orcust monsters", id: "ab"), new([a], 6, "Orcust spells", id: "a"),
            new([b, d], 6, "World Legacy monsters", id: "bd"), new([b], 6, "Other monsters", id: "b"), new([], 16, "Other cards", id: "f")];
        ComboCategory ab = ComboCategory.SameCard([a, b], 1, 0, RequirementMaximumMode.HandSize);
        List<(string Name, List<Combo> Combos, bool Advanced)> cases = [
            ("atomic", [new([new(a, 1, 5), new(b, 1, 5)])], false),
            ("and", [new([ab, new(c, 1, 5)])], false),
            ("or-and", [new([], alternativeGroups: [new([ComboAlternative.For(ab), ComboAlternative.For(ComboCategory.SameCard([b, d], 1, 5))])])], false),
            ("advanced-or-and", [new([ab]), new([new(c, 1, 5)])], true)
        ];
        SessionService codec = new(Mock.Of<IJSRuntime>(), new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());
        List<object> exported = [];
        string directory = Environment.GetEnvironmentVariable("YGO_DRAW_BENCHMARK_OUTPUT")!;
        Directory.CreateDirectory(directory);
        foreach ((string name, List<Combo> combos, bool advanced) in cases) {
            List<Card> cards = deck.ToList();
            if (advanced) {
                cards[^1] = cards[^1].WithCopies(15);
                cards.Insert(0, new([], name: "Draw and remove", id: "effect", drawCount: 2, postDrawDiscardCount: 1,
                    postDrawSelector: SameCardSelectorTest.Composite));
            }
            SessionState session = new() { Cards = cards, Categories = [c], Combos = combos, HandSize = 5 };
            File.WriteAllText(Path.Combine(directory, name + ".json"), codec.SerializeSession(session));
            WorkBudget budget = new(CalculationWorkPolicy.Interactive);
            ExactCalculationResult result = advanced ? new PostDrawCalculation(cards, 5, budget).Calculate(combos, [])
                : new ProbabilityCalculation(cards, 5, budget).CalculateExactResults(combos, []);
            CalculationSnapshot snapshot = CalculationSnapshot.Capture(cards, combos, 5, [], CalculationWorkPolicy.Interactive, options: new());
            exported.Add(new { Name = name, Outcome = "Success", Advanced = advanced, Work = budget.Spent,
                Probability = result.Total.ToDouble(budget), Exact = $"{result.Total.Numerator}/{result.Total.Denominator}",
                Session = JsonSerializer.Deserialize<JsonElement>(codec.SerializeSession(session)),
                Input = JsonSerializer.Deserialize<JsonElement>(snapshot.Json) });
        }
        File.WriteAllText(Path.Combine(directory, "same-card-worker-cases.json"), JsonSerializer.Serialize(exported));
    }
}

// This benchmark also compiles unchanged at the pre-AND baseline.
[TestFixture, Explicit("Fixed selector benchmark; tiered compilation disabled by the caller")]
public class SelectorPerformanceBenchmark {
    [Test]
    public async Task MeasureSelectorFixtures() {
        string directory = Environment.GetEnvironmentVariable("YGO_DRAW_BENCHMARK_OUTPUT")!;
        bool baseline = Environment.GetEnvironmentVariable("YGO_SELECTOR_BASELINE") == "1";
        SessionService codec = new(Mock.Of<IJSRuntime>(), new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());
        foreach (string name in baseline ? new[] { "atomic" } : new[] { "atomic", "and", "or-and", "advanced-or-and" }) {
            SessionState session = await codec.LoadSessionAsync(File.ReadAllText(Path.Combine(directory, name + ".json")));
            List<double> times = [];
            List<long> allocations = [];
            long work = 0;
            ExactProbability expected = default;
            for (int trial = -5; trial < 15; trial++) {
                long before = GC.GetAllocatedBytesForCurrentThread();
                Stopwatch timer = Stopwatch.StartNew();
                WorkBudget budget = new(CalculationWorkPolicy.Interactive);
                ExactProbability result = name == "advanced-or-and"
                    ? new PostDrawCalculation(session.Cards, 5, budget).Calculate(session.Combos, []).Total
                    : new ProbabilityCalculation(session.Cards, 5, budget).CalculateUnionExact(session.Combos);
                timer.Stop();
                if (trial > -5) {
                    Assert.That(result.Numerator * expected.Denominator, Is.EqualTo(expected.Numerator * result.Denominator));
                }
                expected = result;
                work = budget.Spent;
                if (trial >= 0) {
                    times.Add(timer.Elapsed.TotalMilliseconds);
                    allocations.Add(GC.GetAllocatedBytesForCurrentThread() - before);
                }
            }
            TestContext.Out.WriteLine($"{name}: median={times.Order().ElementAt(7):F4}ms; allocated={allocations.Order().ElementAt(7)}; work={work}; exact={expected.Numerator}/{expected.Denominator}");
        }
    }
}
