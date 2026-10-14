using System.Diagnostics;
using System.Text.Json;
using Microsoft.JSInterop;
using Moq;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

public partial class DrawEffectsBenchmark {
    [Test]
    public async Task ExportRealisticThreeGracefulWorkerCases() {
        SessionState session = await RealisticGracefulFixture.Load();
        YGOProbabilityCalculatorBlazor.Services.Session.SessionService codec = new(
            Mock.Of<IJSRuntime>(), new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());
        List<object> exported = [];
        foreach ((string name, bool global, bool conditional, CalculationStorageTier tier) in new[] {
            ("realistic-three-graceful", true, true, CalculationStorageTier.Standard),
            ("realistic-three-graceful-high", true, true, CalculationStorageTier.High),
            ("realistic-three-graceful-very-high", true, true, CalculationStorageTier.VeryHigh),
            ("realistic-three-graceful-conditional-off", true, false, CalculationStorageTier.Standard),
            ("realistic-three-graceful-global-off", false, true, CalculationStorageTier.Standard),
            ("realistic-three-graceful-both-off", false, false, CalculationStorageTier.Standard)
        }) {
            CalculationStoragePolicy storagePolicy = CalculationStoragePolicy.ForTier(tier);
            WorkBudget budget = new(CalculationWorkPolicy.Interactive, storagePolicy);
            PostDrawCalculation calculation = new(session.Cards, session.HandSize, budget);
            Stopwatch timer = Stopwatch.StartNew();
            string outcome = "Success";
            ProbabilityCalculationResult? reference = null;
            ExactCalculationResult? exact = null;
            List<Card> modeled = conditional ? session.Cards : session.Cards.Select(card => card.WithPostDrawDiscardCount(0)).ToList();
            SessionState modeledSession = new() {
                Cards = modeled, Categories = session.Categories, Combos = session.Combos,
                ComboGroups = session.ComboGroups, HandSize = session.HandSize,
                DrawEffectsEnabled = global, DrawActivationPriority = session.DrawActivationPriority,
                CategoryColorIndices = session.CategoryColorIndices
            };
            CalculationSnapshot snapshot = CalculationSnapshot.Capture(modeled, session.Combos,
                session.HandSize, session.ComboGroups, CalculationWorkPolicy.Interactive, global, storagePolicy, new());
            try {
                if (global && conditional) {
                    exact = calculation.Calculate(session.Combos, session.ComboGroups);
                    reference = exact.ToPublic(session.Combos, session.ComboGroups, budget);
                }
                else {
                    exact = global
                        ? new DrawEffectCalculation(modeled, session.HandSize, budget).Calculate(session.Combos, session.ComboGroups)
                        : new ProbabilityCalculation(session.Cards.Select(card => card.WithDrawCount(null)).ToList(), session.HandSize, budget)
                            .CalculateExactResults(session.Combos, session.ComboGroups);
                    reference = exact.ToPublic(session.Combos, session.ComboGroups, budget);
                }
                ProbabilityCalculationResult worker = CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json));
                Assert.That(worker.TotalProbability, Is.EqualTo(reference.TotalProbability));
                Assert.That(worker.ComboProbabilities.Select(row => row.Probability), Is.EqualTo(reference.ComboProbabilities.Select(row => row.Probability)));
                Assert.That(worker.GroupProbabilities.Select(row => row.Probability), Is.EqualTo(reference.GroupProbabilities.Select(row => row.Probability)));
            }
            catch (ProbabilityCalculationLimitException error) {
                outcome = error.Reason.ToString();
            }
            timer.Stop();
            object measurement = new {
                Name = name, Outcome = outcome, Work = budget.Spent,
                calculation.TerminalStates, calculation.PeakStates, calculation.PeakCells,
                calculation.LargestStructure, calculation.ReducedRows,
                Milliseconds = timer.Elapsed.TotalMilliseconds, Probability = reference?.TotalProbability,
                ReferenceResults = reference,
                ExactResults = exact is null ? null : new[] { exact.Total }.Concat(exact.Combos).Concat(exact.Groups)
                    .Select(value => $"{value.Numerator}/{value.Denominator}").ToArray(),
                Session = JsonSerializer.Deserialize<JsonElement>(codec.SerializeSession(modeledSession)),
                Input = JsonSerializer.Deserialize<JsonElement>(snapshot.Json)
            };
            exported.Add(measurement);
            TestContext.Out.WriteLine($"{name}: {outcome}; work={budget.Spent}; terminal={calculation.TerminalStates}; peak={calculation.PeakStates}; cells={calculation.PeakCells}; ms={timer.Elapsed.TotalMilliseconds:F2}");
        }
        string? directory = Environment.GetEnvironmentVariable("YGO_DRAW_BENCHMARK_OUTPUT");
        if (directory is not null) {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "realistic-graceful-worker-cases.json"), JsonSerializer.Serialize(exported));
        }
    }

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
                List<Card> modeled = enabled ? deck : deck.Select(card => card.WithPostDrawDiscardCount(0)).ToList();
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
                        result = new DrawEffectCalculation(modeled, 5, budget).Calculate(active, groups);
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
                    List<Card> effects = modeled.Where(card => card.DrawCount is not null).ToList();
                    int ordinary = deck.Where(card => card.DrawCount is null).Sum(card => card.Copies);
                    terminalStates = DrawEffectResolver.Resolve(ordinary, effects, 5, new(CalculationWorkPolicy.Interactive)).Scenarios.Count;
                }
                string name = $"post-{kind}-{(enabled ? "enabled" : "disabled")}";
                CalculationSnapshot snapshot = CalculationSnapshot.Capture(modeled, active, 5, groups,
                    CalculationWorkPolicy.Interactive, options: new());
                exported.Add(new {
                    Name = name, Outcome = outcome, Work = budget.Spent,
                    TerminalStates = terminalStates, PeakStates = conditional?.PeakStates,
                    PeakCells = conditional?.PeakCells, LargestStructure = conditional?.LargestStructure,
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
