using System.Diagnostics;
using System.Text.Json;
using Microsoft.JSInterop;
using Moq;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.DeckImport;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.Session;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

public partial class DrawEffectsBenchmark {
    [Test]
    public void ExportPreDrawWorkerCases() {
        CategoryBase starter = new("Starter");
        CategoryBase extender = new("Extender");
        CategoryBase monster = new CategoryBase("Monster", CategorySource.Metadata, "kind:monster");
        CategoryBase level = new CategoryBase("Level 8", CategorySource.Metadata, "level:8");
        CategoryBase orcust = new CategoryBase("Archetype: Orcust", CategorySource.Metadata, "archetype:orcust");
        CategoryBase legacy = new CategoryBase("Archetype: World Legacy", CategorySource.Metadata, "archetype:world%20legacy");
        List<Combo> combos = [
            new([new(starter, 1, 0, RequirementMaximumMode.HandSize), new(extender, 1, 0, RequirementMaximumMode.HandSize)], "Starter + extender", groupId: "g"),
            new([new(starter, 2, 0, RequirementMaximumMode.HandSize)], "Two starters", groupId: "g"),
            new([], "Starter OR extender", alternativeGroups: [new([ComboAlternative.For(new ComboCategory(starter, 1, 2)),
                ComboAlternative.For(new ComboCategory(extender, 2, 3))])])
        ];
        SessionService codec = new(Mock.Of<IJSRuntime>(), new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());
        List<object> exported = [];
        foreach (int id in new[] { 38120068, 26845680 }) {
            CuratedDrawEffectPreset preset = CuratedDrawEffectPresets.All.Single(value => value.CardId == id);
            Card effect = new([], 3, preset.Name, id: "effect", externalCardId: id,
                drawCount: preset.DrawCount, drawOncePerTurn: preset.DrawOncePerTurn,
                preDrawRemovalCount: preset.PreDrawRemovalCount, preDrawSelector: preset.PreDrawSelector);
            List<Card> deck = id == 38120068
                ? [effect, new([starter, level, monster], 6, "Level 8 starters", id: "a"),
                    new([extender, monster], 6, "Extenders", id: "b"), new([level, monster], 6, "Other Level 8 monsters", id: "c"),
                    new([starter], 3, "Other starters", id: "d"), new([], 16, "Other cards", id: "z")]
                : [effect, new([starter, orcust, monster], 6, "Orcust starters", id: "a"),
                    new([extender, legacy, monster], 6, "World Legacy extenders", id: "b"),
                    new([orcust, monster], 3, "Other Orcust monsters", id: "c"), new([starter, orcust], 3, "Orcust spells", id: "d"),
                    new([extender], 3, "Other extenders", id: "e"), new([], 16, "Other cards", id: "z")];
            Assert.That(deck.Sum(card => card.Copies), Is.EqualTo(40));
            SessionState session = new() { Cards = deck, Categories = [starter, extender], Combos = combos,
                ComboGroups = [new("g", "Routes")], HandSize = 5 };
            foreach (bool advanced in new[] { false, true }) {
                Measure($"pre-draw-{id}-{(advanced ? "advanced" : "basic")}", session, advanced, CalculationStorageTier.Standard);
            }
            foreach (CalculationStorageTier tier in new[] { CalculationStorageTier.High, CalculationStorageTier.VeryHigh }) {
                Measure($"pre-draw-{id}-{tier}", session, true, tier);
            }
            foreach (CategoryBase[] material in new CategoryBase[][] { [], [level], [level, monster], [orcust], [orcust, monster], [legacy, monster] }) {
                string label = material.Length == 0 ? "none" : string.Join("-", material.Select(category => category.MetadataKey));
                SessionState small = new() { Cards = [effect.WithCopies(1), new(material, name: "Material", id: "material"),
                    new([], name: "Target", id: "target"), new([], name: "Filler", id: "filler")],
                    Combos = [new([], "Target", cards: [new("target", 1, 1)])], HandSize = 2 };
                foreach (bool advanced in new[] { false, true }) {
                    Measure($"small-{id}-{label}-{advanced}", small, advanced, CalculationStorageTier.Standard);
                }
            }
        }
        string? directory = Environment.GetEnvironmentVariable("YGO_DRAW_BENCHMARK_OUTPUT");
        if (directory is not null) {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "pre-draw-worker-cases.json"), JsonSerializer.Serialize(exported));
        }

        void Measure(string name, SessionState session, bool advanced, CalculationStorageTier tier) {
            WorkBudget budget = new(CalculationWorkPolicy.Interactive, CalculationStoragePolicy.ForTier(tier));
            PostDrawCalculation calculation = new(session.Cards, session.HandSize, budget);
            Stopwatch timer = Stopwatch.StartNew();
            ExactCalculationResult? exact = null;
            ProbabilityCalculationResult? reference = null;
            string outcome = "Success";
            try {
                exact = advanced ? calculation.Calculate(session.Combos, session.ComboGroups)
                    : new DrawEffectCalculation(session.Cards, session.HandSize, budget, false).Calculate(session.Combos, session.ComboGroups);
                reference = exact.ToPublic(session.Combos, session.ComboGroups, budget);
            }
            catch (ProbabilityCalculationLimitException error) {
                outcome = error.Reason.ToString();
            }
            timer.Stop();
            CalculationSnapshot snapshot = CalculationSnapshot.Capture(session.Cards, session.Combos, session.HandSize, session.ComboGroups,
                CalculationWorkPolicy.Interactive, storagePolicy: CalculationStoragePolicy.ForTier(tier), options: new(advanced));
            if (reference is not null) {
                ProbabilityCalculationResult worker = CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json));
                Assert.That(worker.TotalProbability, Is.EqualTo(reference.TotalProbability));
                Assert.That(worker.ComboProbabilities.Select(row => row.Probability), Is.EqualTo(reference.ComboProbabilities.Select(row => row.Probability)));
                Assert.That(worker.GroupProbabilities.Select(row => row.Probability), Is.EqualTo(reference.GroupProbabilities.Select(row => row.Probability)));
            }
            exported.Add(new {
                Name = name, Outcome = outcome, Work = budget.Spent,
                calculation.TerminalStates, calculation.PeakStates, calculation.PeakCells, calculation.LargestStructure, calculation.ReducedRows,
                Milliseconds = timer.Elapsed.TotalMilliseconds, Probability = reference?.TotalProbability, ReferenceResults = reference,
                ExactResults = exact is null ? null : new[] { exact.Total }.Concat(exact.Combos).Concat(exact.Groups)
                    .Select(value => $"{value.Numerator}/{value.Denominator}").ToArray(),
                Session = JsonSerializer.Deserialize<JsonElement>(codec.SerializeSession(session)),
                Input = JsonSerializer.Deserialize<JsonElement>(snapshot.Json)
            });
            TestContext.Out.WriteLine($"{name}: {outcome}; work={budget.Spent}; terminal={calculation.TerminalStates}; peak={calculation.PeakStates}; cells={calculation.PeakCells}; ms={timer.Elapsed.TotalMilliseconds:F2}");
            Assert.That(outcome, Is.EqualTo("Success"), name);
        }
    }
}
