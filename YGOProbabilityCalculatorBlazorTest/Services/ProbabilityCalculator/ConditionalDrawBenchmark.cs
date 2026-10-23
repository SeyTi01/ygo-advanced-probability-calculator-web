using System.Diagnostics;
using System.Text.Json;
using Microsoft.JSInterop;
using Moq;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.Session;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.DrawResolution;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

internal static class RealisticAllureFixture {
    internal static async Task<SessionState> Load(int copies) {
        SessionService codec = new(Mock.Of<IJSRuntime>(), new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());
        SessionState session = await codec.LoadSessionAsync(File.ReadAllText(Path.Combine(
            TestContext.CurrentContext.TestDirectory, "Fixtures", "example_session_state.json")));
        Card original = session.Cards.Single(card => card.Name == "Mulcharmy Fuwalos");
        Assert.That(session.Combos.SelectMany(combo => combo.AllCards).Any(card => card.CardId == original.Id), Is.False);
        MaintainedCardEffect effect = CardEffectRegistry.Default.Find(1475311)!;
        Card allure = new Card([new("Spell", CategorySource.Metadata, "kind:spell"),
            new("Normal Spell", CategorySource.Metadata, "spell-type:normal")], copies, effect.Name,
            id: "benchmark-allure", externalCardId: effect.CanonicalCardId).WithEffect(effect.Plan, effect.Profile);
        int index = session.Cards.IndexOf(original);
        session.Cards[index] = allure;
        if (copies < original.Copies) {
            session.Cards.Insert(index + 1, original.WithCopies(original.Copies - copies));
        }
        List<Card> materialized = CategoryMembership.Materialize(session.Cards, session.Categories).ToList();
        session.Cards.Clear();
        session.Cards.AddRange(materialized);
        Assert.Multiple(() => {
            Assert.That(session.Cards.Sum(card => card.Copies), Is.EqualTo(40));
            Assert.That(session.HandSize, Is.EqualTo(5));
            Assert.That(session.Combos, Has.Count.EqualTo(8));
            Assert.That(session.ComboGroups, Has.Count.EqualTo(2));
        });
        return session;
    }
}

public partial class DrawEffectsBenchmark {
    // Export with YGO_DRAW_BENCHMARK_OUTPUT. The reference switch disables only
    // the new factorization; tier/work overrides apply solely to this opt-in run.
    [Test]
    public async Task ExportConditionalCases() {
        bool reference = Environment.GetEnvironmentVariable("YGO_CONDITIONAL_REFERENCE") == "1";
        CalculationStorageTier tier = Enum.TryParse(Environment.GetEnvironmentVariable("YGO_CONDITIONAL_TIER"), out CalculationStorageTier selected)
            ? selected : CalculationStorageTier.Standard;
        CalculationWorkPolicy policy = long.TryParse(Environment.GetEnvironmentVariable("YGO_DRAW_BENCHMARK_WORK"), out long work)
            ? new(work) : CalculationWorkPolicy.Interactive;
        SessionService codec = new(Mock.Of<IJSRuntime>(), new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());
        List<object> output = [];
        foreach (int copies in new[] { 1, 3 }) {
            SessionState session = await RealisticAllureFixture.Load(copies);
            foreach (string mode in new[] { "configured", "opening", "greed", "extravagance", "desires", "graceful" }) {
                List<Card> deck = session.Cards;
                int? canonical = mode switch { "greed" => 55144522, "extravagance" => 49238328,
                    "desires" => 35261759, "graceful" => 79571449, _ => null };
                if (canonical is int id) {
                    MaintainedCardEffect effect = CardEffectRegistry.Default.Find(id)!;
                    deck = session.Cards.Select(card => card.Id == "benchmark-allure" ? card.WithEffect(effect.Plan, effect.Profile) : card).ToList();
                }
                List<double> times = [];
                List<long> allocations = [];
                WorkBudget budget = null!;
                PostDrawCalculation post = null!;
                ExactCalculationResult? result = null;
                string outcome = "Success";
                for (int trial = 0; trial < 4; trial++) {
                    budget = new(policy, CalculationStoragePolicy.ForTier(tier));
                    post = new(deck, 5, budget, deferIdentities: !reference);
                    long allocated = GC.GetAllocatedBytesForCurrentThread();
                    Stopwatch timer = Stopwatch.StartNew();
                    try {
                        result = mode == "configured" ? post.Calculate(session.Combos, session.ComboGroups)
                            : mode == "opening" ? new ProbabilityCalculation(deck, 5, budget).CalculateExactResults(session.Combos, session.ComboGroups)
                            : new DrawEffectCalculation(deck, 5, budget).Calculate(session.Combos, session.ComboGroups);
                    }
                    catch (ProbabilityCalculationLimitException exception) {
                        outcome = exception.Reason.ToString();
                    }
                    timer.Stop();
                    allocations.Add(GC.GetAllocatedBytesForCurrentThread() - allocated);
                    times.Add(timer.Elapsed.TotalMilliseconds);
                }
                string name = $"allure-{copies}-{mode}";
                TestContext.Out.WriteLine($"{name}: {outcome}; work={budget.Spent}; states={post.PeakStates}; cells={post.PeakCells}; allCells={budget.PeakCells}; rows={post.ReducedRows}; terminal={post.TerminalStates}; ms={string.Join(',', times)}; allocated={string.Join(',', allocations)}");
                CalculationSnapshot snapshot = CalculationSnapshot.Capture(deck, session.Combos, 5, session.ComboGroups,
                    policy, mode != "opening", storagePolicy: CalculationStoragePolicy.ForTier(tier), options: new());
                ProbabilityCalculationResult? publicResult = result?.ToPublic(session.Combos, session.ComboGroups, budget);
                output.Add(new { Name = name, Outcome = outcome, Work = budget.Spent, Reference = reference, Tier = tier.ToString(),
                    PeakStates = post.PeakStates, PeakCells = post.PeakCells, AllStructuresPeakEntries = budget.PeakEntries,
                    AllStructuresPeakCells = budget.PeakCells,
                    ReducedRows = post.ReducedRows, Milliseconds = times, AllocatedBytes = allocations,
                    Path = (mode == "configured" && reference ? DrawExecutionPath.OrderedFrontier
                        : DrawExecutionAssessment.Assess(deck, session.Combos, 5, mode != "opening").Path).ToString(),
                    ExactResults = result is null ? null : new[] { result.Total }.Concat(result.Combos).Concat(result.Groups)
                        .Select(value => $"{value.Numerator}/{value.Denominator}").ToArray(),
                    Probability = publicResult?.TotalProbability, ReferenceResults = publicResult,
                    Session = mode == "configured" ? JsonSerializer.Deserialize<JsonElement>(codec.SerializeSession(session)) : (JsonElement?)null,
                    Input = JsonSerializer.Deserialize<JsonElement>(snapshot.Json) });
            }
        }
        string directory = Environment.GetEnvironmentVariable("YGO_DRAW_BENCHMARK_OUTPUT")!;
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "conditional-worker-cases.json"), JsonSerializer.Serialize(output));
    }
}
