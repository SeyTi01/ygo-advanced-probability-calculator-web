using System.Diagnostics;
using System.Text.Json;
using Microsoft.JSInterop;
using Moq;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.Session;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

internal static class RealisticSolarFixture {
    internal static async Task<SessionState> Load(int copies) {
        SessionState session = await RealisticAllureFixture.Load(copies);
        MaintainedCardEffect solar = CardEffectRegistry.Default.Find(691925)!;
        int sourceIndex = session.Cards.FindIndex(card => card.Id == "benchmark-allure");
        session.Cards[sourceIndex] = new Card([
            new("Spell", CategorySource.Metadata, "kind:spell"),
            new("Normal Spell", CategorySource.Metadata, "spell-type:normal")
        ], copies, solar.Name, id: "benchmark-solar", externalCardId: solar.CanonicalCardId).WithEffect(solar.Plan, solar.Profile);

        // Maxx "C" and Ryko have no original combo role. Unlike replacing Ash,
        // this preserves every FIRE/DARK/Level 5 membership in the VS/K9 fixture.
        Card original = session.Cards.Single(card => card.Name == "Maxx \"C\"");
        Assert.That(original.Copies, Is.EqualTo(1));
        Assert.That(session.Combos.SelectMany(combo => combo.AllCards).Any(card => card.CardId == original.Id), Is.False);
        Card ryko = new([
            new("Monster", CategorySource.Metadata, "kind:monster"),
            new("Flip Monster", CategorySource.Metadata, "monster-trait:flip"),
            new("Effect Monster", CategorySource.Metadata, "monster-trait:effect"),
            new("Attribute: LIGHT", CategorySource.Metadata, "attribute:light"),
            new("Monster Type: Beast", CategorySource.Metadata, "monster-race:beast"),
            new("Level 2", CategorySource.Metadata, "level:2"),
            new("Archetype: Lightsworn", CategorySource.Metadata, "archetype:lightsworn")
        ], 1, "Ryko, Lightsworn Hunter", id: "benchmark-ryko", externalCardId: 21502796);
        Assert.That(session.Combos.SelectMany(combo => combo.AllCategories)
            .Any(requirement => requirement.Selector.Matches(original.Categories) || requirement.Selector.Matches(ryko.Categories)), Is.False);
        session.Cards[session.Cards.IndexOf(original)] = ryko;
        List<Card> materialized = CategoryMembership.Materialize(session.Cards, session.Categories).ToList();
        session.Cards.Clear();
        session.Cards.AddRange(materialized);
        Assert.Multiple(() => {
            Assert.That(session.Cards.Sum(card => card.Copies), Is.EqualTo(40));
            Assert.That(session.HandSize, Is.EqualTo(5));
            Assert.That(session.Combos, Has.Count.EqualTo(8));
            Assert.That(session.ComboGroups.Select(group => group.Id), Is.EqualTo(new[] {
                "48c6559aefb544fb949e8aabef2d251d", "0523b13956664dc7b0a36d0ec6c54b01"
            }));
            Assert.That(session.Cards.Where(card => solar.Plan.HandCostSelector.Matches(card.Categories)).Sum(card => card.Copies), Is.EqualTo(1));
        });

        return session;
    }
}

public partial class DrawEffectsBenchmark {
    // YGO_CONDITIONAL_REFERENCE=1 disables pooling, leaving all budgets unchanged.
    // Four trials include the first native call and three warm observations.
    // PeakEntries/Cells are per-structure checked payloads, not process peak RAM.
    [Test]
    public async Task ExportPreDrawConditionalCases() {
        bool reference = Environment.GetEnvironmentVariable("YGO_CONDITIONAL_REFERENCE") == "1";
        SessionService codec = new(Mock.Of<IJSRuntime>(), new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());
        List<(string Name, SessionState Session, string Mode)> cases = [];
        foreach (int copies in new[] { 1, 3 }) {
            SessionState solar = await RealisticSolarFixture.Load(copies);
            foreach (string mode in new[] { "configured", "opening", "simplified" }) {
                cases.Add(($"solar-{copies}-{mode}", solar, mode));
            }

            SessionState vision = await RealisticAllureFixture.Load(copies);
            MaintainedCardEffect effect = CardEffectRegistry.Default.Find(20508881)!;
            int index = vision.Cards.FindIndex(card => card.Id == "benchmark-allure");
            vision.Cards[index] = new Card([
                new("Spell", CategorySource.Metadata, "kind:spell"),
                new("Quick-Play Spell", CategorySource.Metadata, "spell-type:quick-play"),
                new("Archetype: Radiant Typhoon", CategorySource.Metadata, "archetype:radiant%20typhoon")
            ], copies, effect.Name, id: "benchmark-vision", externalCardId: effect.CanonicalCardId).WithEffect(effect.Plan, effect.Profile);
            cases.Add(($"vision-{copies}-configured", vision, "configured"));
        }

        List<object> output = [];
        foreach ((string name, SessionState session, string mode) in cases) {
            List<double> times = [];
            List<long> allocations = [];
            WorkBudget budget = null!;
            PostDrawCalculation? post = null;
            ExactCalculationResult? result = null;
            string outcome = "Success";
            for (int trial = 0; trial < 4; trial++) {
                budget = new(CalculationWorkPolicy.Interactive, CalculationStoragePolicy.ForTier(CalculationStorageTier.Standard));
                post = mode == "configured" ? new(session.Cards, session.HandSize, budget, deferIdentities: !reference) : null;
                result = null;
                long allocated = GC.GetAllocatedBytesForCurrentThread();
                Stopwatch timer = Stopwatch.StartNew();
                try {
                    result = post is not null ? post.Calculate(session.Combos, session.ComboGroups)
                        : mode == "opening" ? new ProbabilityCalculation(session.Cards, session.HandSize, budget).CalculateExactResults(session.Combos, session.ComboGroups)
                        : new DrawEffectCalculation(session.Cards, session.HandSize, budget, false).Calculate(session.Combos, session.ComboGroups);
                    outcome = "Success";
                }
                catch (ProbabilityCalculationLimitException exception) {
                    outcome = exception.Reason.ToString();
                }
                timer.Stop();
                allocations.Add(GC.GetAllocatedBytesForCurrentThread() - allocated);
                times.Add(timer.Elapsed.TotalMilliseconds);
            }

            CalculationSnapshot snapshot = CalculationSnapshot.Capture(
                session.Cards, session.Combos, session.HandSize, session.ComboGroups,
                CalculationWorkPolicy.Interactive, mode != "opening",
                storagePolicy: CalculationStoragePolicy.ForTier(CalculationStorageTier.Standard), options: new(mode != "simplified")
            );
            ProbabilityCalculationResult? publicResult = result?.ToPublic(session.Combos, session.ComboGroups, budget);
            TestContext.Out.WriteLine($"{name}: {outcome}; reference={reference}; work={budget.Spent}; states={post?.PeakStates}; cells={post?.PeakCells}; allCells={budget.PeakCells}; rows={post?.ReducedRows}; deferred={post?.DeferredRows}; terminal={post?.TerminalStates}; ms={string.Join(',', times)}; allocated={string.Join(',', allocations)}");
            output.Add(new {
                Name = name, Outcome = outcome, Work = budget.Spent, Reference = reference, Tier = "Standard",
                PeakStates = post?.PeakStates ?? 0, PeakCells = post?.PeakCells ?? 0,
                AllStructuresPeakEntries = budget.PeakEntries, AllStructuresPeakCells = budget.PeakCells,
                StorageMetric = "Peak per-structure checked payload; not whole-process peak memory",
                ReducedRows = post?.ReducedRows ?? 0,
                ExplicitRows = post is null ? 0 : post.ReducedRows - (post.DeferredRows > 0 ? 1 : 0),
                DeferredRows = post?.DeferredRows ?? 0, TerminalStates = post?.TerminalStates ?? 0,
                Milliseconds = times, AllocatedBytes = allocations,
                Path = (mode == "configured" && reference ? DrawExecutionPath.OrderedFrontier
                    : DrawExecutionAssessment.Assess(session.Cards, session.Combos, session.HandSize, mode != "opening", mode != "simplified").Path).ToString(),
                ExactResults = result is null ? null : new[] { result.Total }.Concat(result.Combos).Concat(result.Groups)
                    .Select(value => $"{value.Numerator}/{value.Denominator}").ToArray(),
                Probability = publicResult?.TotalProbability, ReferenceResults = publicResult,
                Session = mode == "configured" ? JsonSerializer.Deserialize<JsonElement>(codec.SerializeSession(session)) : (JsonElement?)null,
                Input = JsonSerializer.Deserialize<JsonElement>(snapshot.Json)
            });
        }

        string directory = Environment.GetEnvironmentVariable("YGO_DRAW_BENCHMARK_OUTPUT")
            ?? throw new InvalidOperationException("Set YGO_DRAW_BENCHMARK_OUTPUT.");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "solar-worker-cases.json"), JsonSerializer.Serialize(output));
    }
}
