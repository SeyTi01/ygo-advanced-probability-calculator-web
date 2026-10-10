using System.Text.Json;
using Microsoft.JSInterop;
using Moq;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.Session;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

// Export the exact worker inputs and reference results for a published-browser run.
// Timing belongs to the actual WASM worker, never to a regression assertion.
[TestFixture, Explicit("Draw-effect worker benchmark fixtures")]
public class DrawEffectsBenchmark {
    [Test]
    public async Task ExportWorkerCases() {
        CategoryBase starter = new("Starter");
        CategoryBase extender = new("Extender");
        List<Combo> combos = [new([new(starter, 1, 0, RequirementMaximumMode.HandSize),
            new(extender, 1, 0, RequirementMaximumMode.HandSize)], "Route", groupId: "g")];
        List<ComboGroup> groups = [new("g", "Group")];
        List<(string Name, SessionState Session)> cases = [];
        foreach ((string name, (int Copies, int Draw)[] effects) in new (string, (int, int)[])[] {
            ("no-draw", []), ("one-draw2", [(1, 2)]), ("three-copies-draw2", [(3, 2)]),
            ("three-identities", [(3, 2), (3, 2), (3, 2)]), ("mixed-123", [(3, 1), (3, 2), (3, 3)])
        }) {
            List<Card> cards = [new([starter], 9, id: "starter"), new([extender], 9, id: "extender"),
                new([], 22 - effects.Sum(effect => effect.Copies), id: "blank")];
            for (int i = 0; i < effects.Length; i++) {
                cards.Add(new([starter, extender], effects[i].Copies, id: $"draw{i}", drawCount: effects[i].Draw));
            }

            cases.Add((name, new() { Cards = cards, Combos = combos, ComboGroups = groups, HandSize = 5 }));
        }

        SessionService codec = new(Mock.Of<IJSRuntime>(), new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());
        string exampleJson = File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "example_session_state.json"));
        SessionState example = await codec.LoadSessionAsync(exampleJson);
        cases.Add(("example-baseline", example));
        SessionState configured = await codec.LoadSessionAsync(exampleJson);
        for (int i = 0; i < 3; i++) {
            configured.Cards[i] = configured.Cards[i].WithDrawCount(i + 1);
        }

        cases.Add(("example-mixed-123", configured));
        List<object> exported = [];
        foreach ((string name, SessionState session) in cases) {
            List<Card> deck = session.Cards.Where(card => card.Active).ToList();
            List<Combo> activeCombos = session.Combos.Where(combo => combo.Active).ToList();
            List<Card> effects = deck.Where(card => card.DrawCount is not null).ToList();
            int ordinary = deck.Where(card => card.DrawCount is null).Sum(card => card.Copies);
            DrawResolution resolution = DrawEffectResolver.Resolve(ordinary, effects, session.HandSize, new(CalculationWorkPolicy.Interactive));
            WorkBudget budget = new(CalculationWorkPolicy.Interactive);
            string outcome = "Success";
            double? probability = null;
            try {
                probability = new DrawEffectCalculation(deck, session.HandSize, budget)
                    .Calculate(activeCombos, session.ComboGroups).ToPublic(activeCombos, session.ComboGroups, budget).TotalProbability;
            }
            catch (ProbabilityCalculationLimitException exception) {
                outcome = exception.Reason.ToString();
            }

            CalculationSnapshot snapshot = CalculationSnapshot.Capture(deck, activeCombos, session.HandSize, session.ComboGroups, CalculationWorkPolicy.Interactive);
            exported.Add(new { Name = name, Scenarios = resolution.Scenarios.Count, Work = budget.Spent, Outcome = outcome,
                Probability = probability, Input = JsonSerializer.Deserialize<JsonElement>(snapshot.Json) });
            TestContext.Out.WriteLine($"{name}: scenarios={resolution.Scenarios.Count}; work={budget.Spent}; {outcome}; p={probability:R}");
        }

        string? directory = Environment.GetEnvironmentVariable("YGO_DRAW_BENCHMARK_OUTPUT");
        if (!string.IsNullOrEmpty(directory)) {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "draw-worker-cases.json"), JsonSerializer.Serialize(exported));
        }
    }
}
