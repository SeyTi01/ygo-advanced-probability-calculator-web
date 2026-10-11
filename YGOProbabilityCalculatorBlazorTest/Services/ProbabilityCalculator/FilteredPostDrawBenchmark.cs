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
    public void ExportFilteredPostDrawWorkerCases() {
        CategoryBase starter = new("Starter");
        CategoryBase extender = new("Extender");
        CategoryBase dark = CuratedDrawEffectPresets.FilterProperties[0];
        CategoryBase quick = CuratedDrawEffectPresets.FilterProperties[1];
        CategoryBase radiant = CuratedDrawEffectPresets.FilterProperties[2];
        List<Combo> combos = [
            new([new(starter, 1, 0, RequirementMaximumMode.HandSize), new(extender, 1, 0, RequirementMaximumMode.HandSize)], "Starter + extender", groupId: "g"),
            new([new(starter, 2, 0, RequirementMaximumMode.HandSize)], "Two starters", groupId: "g"),
            new([], "Starter OR extender", alternativeGroups: [new([ComboAlternative.For(new ComboCategory(starter, 1, 2)),
                ComboAlternative.For(new ComboCategory(extender, 2, 3))])])
        ];
        SessionService codec = new(Mock.Of<IJSRuntime>(), new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());
        List<object> exported = [];
        foreach (int id in new[] { 1475311, 20508881 }) {
            CardInfo info = new() {
                Id = id, Name = id == 1475311 ? "Allure of Darkness" : "Radiant Typhoon Vision",
                Type = "Spell Card", Race = id == 1475311 ? "Normal" : "Quick-Play",
                Archetype = id == 1475311 ? null : "Radiant Typhoon"
            };
            Card imported = CuratedDrawEffectPresets.CreateImportedCard(info, 3, id);
            Card effect = new(imported.Categories, 3, imported.Name, id: "effect", externalCardId: id,
                drawCount: imported.DrawCount, drawOncePerTurn: imported.DrawOncePerTurn,
                postDrawDiscardCount: imported.PostDrawDiscardCount, postDrawSelector: imported.PostDrawSelector);
            List<Card> deck = id == 1475311
                ? [effect, new([starter, dark], 9, "DARK starters", id: "a"), new([extender], 6, "Non-DARK extenders", id: "b"),
                    new([dark], 6, "Other DARK monsters", id: "c"), new([], 16, "Other cards", id: "z")]
                : [effect, new([starter, quick], 6, "Quick-Play starters", id: "a"), new([extender, radiant], 6, "Radiant extenders", id: "b"),
                    new([quick, radiant], 3, "Radiant Quick-Play", id: "c"), new([starter], 6, "Other starters", id: "d"), new([], 16, "Other cards", id: "z")];
            Assert.That(deck.Sum(card => card.Copies), Is.EqualTo(40));
            SessionState session = new() { Cards = deck, Categories = [starter, extender], Combos = combos,
                ComboGroups = [new("g", "Routes")], HandSize = 5 };
            foreach (bool advanced in new[] { false, true }) {
                Measure($"filtered-{id}-{(advanced ? "advanced" : "basic")}", session, advanced, CalculationStorageTier.Standard);
            }
            foreach (CalculationStorageTier tier in new[] { CalculationStorageTier.High, CalculationStorageTier.VeryHigh }) {
                Measure($"filtered-{id}-{tier}", session, true, tier);
            }
            foreach (CategoryBase[] material in new CategoryBase[][] { [], [dark], [quick], [radiant], [quick, radiant] }) {
                string label = material.Length == 0 ? "none" : string.Join("-", material.Select(category => category.MetadataKey));
                SessionState small = new() { Cards = [effect.WithCopies(1), new(material, name: "Material", id: "material"), new([], name: "Target", id: "target")],
                    Combos = [new([], "Any surviving hand", cards: [new("target", 0, 0, RequirementMaximumMode.HandSize)])], HandSize = 1 };
                foreach (bool advanced in new[] { false, true }) {
                    Measure($"small-{id}-{label}-{advanced}", small, advanced, CalculationStorageTier.Standard);
                }
            }
        }
        string? directory = Environment.GetEnvironmentVariable("YGO_DRAW_BENCHMARK_OUTPUT");
        if (directory is not null) {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "filtered-post-draw-worker-cases.json"), JsonSerializer.Serialize(exported));
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
