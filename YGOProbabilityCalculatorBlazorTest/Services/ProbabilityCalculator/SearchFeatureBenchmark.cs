using System.Diagnostics;
using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture, Explicit("Search feature before/after acceptance"), NonParallelizable]
public sealed class SearchFeatureBenchmark {
    [Test]
    public async Task Measure() {
        List<(string Name, SessionState Session, DrawActivationPriority? Priority)> cases = [];
        SessionState ordinary = await UnionBenchmark.LoadModel();
        cases.Add(("ordinary", ordinary, null));
        SessionState draws = await UnionBenchmark.LoadModel();
        int index = draws.Cards.FindIndex(card => card.Name == "Mulcharmy Fuwalos");
        draws.Cards[index] = draws.Cards[index].WithDrawCount(2);
        cases.Add(("count-state", draws, null));
        cases.Add(("three-graceful", await RealisticGracefulFixture.Load(), null));
        SessionState trade = await TradeInAllureFixture.Load();
        cases.Add(("trade-allure-encounter", trade, null));
        cases.Add(("trade-allure-custom", trade, new(DrawActivationMode.CustomPriority, ["benchmark-allure", "benchmark-trade-in"])));
        cases.Add(("deferred-vision", await RealisticMixedFixture.Load(true), null));
        cases.Add(("hidden-desires", await RealisticMixedFixture.Load(false), null));
        string? sample = Environment.GetEnvironmentVariable("YGO_SEARCH_SAMPLE");
        if (sample is not null) {
            YGOProbabilityCalculatorBlazor.Services.Session.SessionService codec = new(Moq.Mock.Of<Microsoft.JSInterop.IJSRuntime>(),
                new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());
            cases.Add(("search-example", await codec.LoadSessionAsync(File.ReadAllText(sample)), null));
            foreach (int drawCopies in new[] { 1, 3 }) {
                SessionState mixed = await codec.LoadSessionAsync(File.ReadAllText(sample));
                int drawIndex = mixed.Cards.FindIndex(card => card.Name == "Mulcharmy Fuwalos");
                Card original = mixed.Cards[drawIndex];
                mixed.Cards[drawIndex] = original.WithCopies(drawCopies).WithDrawCount(2).WithDrawOncePerTurn(false);
                if (drawCopies < original.Copies) {
                    mixed.Cards.Add(original.Duplicate().WithCopies(original.Copies - drawCopies));
                }
                cases.Add(($"search-example-{drawCopies}-draw2", mixed, null));
            }
        }
        List<object> exports = [];
        foreach ((string name, SessionState session, DrawActivationPriority? priority) in cases) {
            List<double> times = [];
            WorkBudget budget = null!;
            ProbabilityCalculationResult? result = null;
            string outcome = "Success";
            for (int trial = 0; trial < 4; trial++) {
                budget = new(CalculationWorkPolicy.Interactive, CalculationStoragePolicy.Standard);
                Stopwatch timer = Stopwatch.StartNew();
                try {
                    result = new DrawEffectCalculation(session.Cards, session.HandSize, budget, priority)
                        .Calculate(session.Combos, session.ComboGroups).ToPublic(session.Combos, session.ComboGroups, budget);
                }
                catch (ProbabilityCalculationLimitException ex) {
                    outcome = ex.Reason.ToString();
                }
                if (trial > 0) {
                    times.Add(timer.Elapsed.TotalMilliseconds);
                }
            }
            TestContext.Out.WriteLine($"{name}: {outcome}; median={times.Order().ElementAt(1):F2}ms; work={budget.Spent}; cells={budget.PeakCells}; p={result?.TotalProbability:R}");
            exports.Add(new { Name = name, Outcome = outcome, Probability = result?.TotalProbability, Work = budget.Spent,
                Cells = budget.PeakCells, Milliseconds = times, ReferenceResults = result,
                Input = JsonSerializer.Deserialize<JsonElement>(CalculationSnapshot.Capture(session.Cards, session.Combos, session.HandSize,
                    session.ComboGroups, CalculationWorkPolicy.Interactive, options: new(priority)).Json) });
        }
        string? output = Environment.GetEnvironmentVariable("YGO_SEARCH_BENCHMARK");
        if (output is not null) {
            File.WriteAllText(output, JsonSerializer.Serialize(exports));
        }
    }
}
