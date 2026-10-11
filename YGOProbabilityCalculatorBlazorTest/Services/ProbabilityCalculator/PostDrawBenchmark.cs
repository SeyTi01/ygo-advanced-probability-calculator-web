using System.Diagnostics;
using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

public partial class DrawEffectsBenchmark {
    [Test]
    public void ExportPostDrawWorkerCases() {
        CategoryBase starter = new("Starter");
        CategoryBase extender = new("Extender");
        List<Combo> combos = [new([new(starter, 1, 0, RequirementMaximumMode.HandSize),
            new(extender, 1, 0, RequirementMaximumMode.HandSize)], groupId: "g")];
        List<ComboGroup> groups = [new("g", "Group")];
        List<object> exported = [];
        foreach (string kind in new[] { "no-draw", "draw-only", "dealings", "graceful", "both", "mixed", "pathological" }) {
            List<Card> deck = [];
            // Several physical deck entries per role prove equivalence reduction.
            for (int i = 0; i < 3; i++) {
                deck.Add(new([starter], 3, id: $"a{i}"));
                deck.Add(new([extender], 3, id: $"b{i}"));
            }
            if (kind is "dealings" or "both" or "mixed" or "pathological") {
                deck.Add(new([], 3, id: "d", drawCount: 1, drawOncePerTurn: false, postDrawDiscardCount: 1));
            }
            if (kind is "graceful" or "both" or "mixed" or "pathological") {
                deck.Add(new([], 3, id: "g", drawCount: 3, drawOncePerTurn: false, postDrawDiscardCount: 2));
            }
            if (kind is "mixed" or "draw-only" or "pathological") {
                deck.Add(new([], 3, id: "p", drawCount: 2));
            }
            if (kind == "draw-only") {
                deck.Add(new([], 3, id: "d", drawCount: 1, drawOncePerTurn: false));
                deck.Add(new([], 3, id: "g", drawCount: 3, drawOncePerTurn: false));
            }
            int filler = 40 - deck.Sum(card => card.Copies);
            for (int i = 0; i < filler; i++) {
                deck.Add(new([], id: $"z{i}"));
            }
            List<Combo> active = kind == "pathological"
                ? deck.Select(card => new Combo([], cards: [new(card.Id, 1, 1)])).Take(25).ToList()
                : combos;
            foreach (bool enabled in new[] { true, false }) {
                WorkBudget budget = new(CalculationWorkPolicy.Interactive);
                PostDrawCalculation? conditional = enabled && deck.Any(card => card.PostDrawDiscardCount > 0)
                    ? new(deck, 5, budget) : null;
                Stopwatch timer = Stopwatch.StartNew();
                string outcome = "Success";
                ProbabilityCalculationResult? reference = null;
                ExactCalculationResult? result = null;
                try {
                    if (conditional is not null) {
                        result = conditional.Calculate(active, groups);
                    }
                    else if (deck.Any(card => card.DrawCount is not null)) {
                        result = new DrawEffectCalculation(deck, 5, budget, enabled).Calculate(active, groups);
                    }
                    else {
                        result = new ProbabilityCalculation(deck, 5, budget).CalculateExactResults(active, groups);
                    }
                    reference = result.ToPublic(active, groups, budget);
                }
                catch (ProbabilityCalculationLimitException error) {
                    outcome = error.Reason.ToString();
                }
                timer.Stop();
                int? terminalStates = conditional?.TerminalStates;
                if (conditional is null) {
                    List<Card> effects = deck.Where(card => card.DrawCount is not null).ToList();
                    int ordinary = deck.Where(card => card.DrawCount is null).Sum(card => card.Copies);
                    terminalStates = DrawEffectResolver.Resolve(ordinary, effects, 5, new(CalculationWorkPolicy.Interactive)).Scenarios.Count;
                }
                string name = $"post-{kind}-{(enabled ? "enabled" : "disabled")}";
                CalculationSnapshot snapshot = CalculationSnapshot.Capture(deck, active, 5, groups,
                    CalculationWorkPolicy.Interactive, options: new(enabled));
                exported.Add(new {
                    Name = name, Outcome = outcome, Work = budget.Spent,
                    TerminalStates = terminalStates, PeakStates = conditional?.PeakStates,
                    Milliseconds = timer.Elapsed.TotalMilliseconds, Probability = reference?.TotalProbability,
                    ReferenceResults = reference,
                    ExactResults = result is null ? null : new[] { result.Total }.Concat(result.Combos).Concat(result.Groups)
                        .Select(value => $"{value.Numerator}/{value.Denominator}").ToArray(),
                    Input = JsonSerializer.Deserialize<JsonElement>(snapshot.Json)
                });
                TestContext.Out.WriteLine($"{name}: {outcome}; work={budget.Spent}; terminal={terminalStates}; peak={conditional?.PeakStates}; ms={timer.Elapsed.TotalMilliseconds:F2}");
                if (kind != "pathological") {
                    Assert.That(outcome, Is.EqualTo("Success"), name);
                }
            }
        }
        string? directory = Environment.GetEnvironmentVariable("YGO_DRAW_BENCHMARK_OUTPUT");
        if (directory is not null) {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "post-draw-worker-cases.json"), JsonSerializer.Serialize(exported));
        }
    }
}
