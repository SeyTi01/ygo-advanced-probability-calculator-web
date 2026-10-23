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
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.DrawResolution;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

internal static class RealisticMixedFixture {
    internal static async Task<SessionState> Load(bool vision) {
        SessionState session = await RealisticAllureFixture.Load(2);
        int index = session.Cards.FindIndex(card => card.Name == "Mulcharmy Fuwalos");
        MaintainedCardEffect effect = CardEffectRegistry.Default.Find(vision ? 20508881 : 35261759)!;
        List<CategoryBase> categories = [new("Spell", CategorySource.Metadata, "kind:spell")];
        categories.Add(new(vision ? "Quick-Play Spell" : "Normal Spell", CategorySource.Metadata,
            vision ? "spell-type:quick-play" : "spell-type:normal"));
        if (vision) {
            categories.Add(new("Archetype: Radiant Typhoon", CategorySource.Metadata, "archetype:radiant%20typhoon"));
        }
        session.Cards[index] = new Card(categories, 1, effect.Name, id: "benchmark-secondary",
            externalCardId: effect.CanonicalCardId).WithEffect(effect.Plan, effect.Profile);
        Assert.Multiple(() => {
            Assert.That(session.Cards.Sum(card => card.Copies), Is.EqualTo(40));
            Assert.That(session.HandSize, Is.EqualTo(5));
            Assert.That(session.Combos, Has.Count.EqualTo(8));
            Assert.That(session.ComboGroups.Select(group => group.Id), Is.EqualTo(new[] {
                "48c6559aefb544fb949e8aabef2d251d", "0523b13956664dc7b0a36d0ec6c54b01"
            }));
            // Vision has legal Quick-Play material in the unchanged example.
            Assert.That(!vision || session.Cards.Any(card => card.Id != "benchmark-secondary"
                && effect.Plan.RemovalSelector.Matches(card.Categories)), Is.True);
        });
        return session;
    }
}

[TestFixture, Explicit("Frozen mixed-effect measurements"), NonParallelizable]
public class MixedDeferredBenchmark {
    [Test]
    public async Task ExportExamples() {
        SessionService codec = new(Mock.Of<IJSRuntime>(), new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());
        List<object> output = [];
        foreach (bool vision in new[] { true, false }) {
            SessionState session = await RealisticMixedFixture.Load(vision);
            foreach (bool custom in new[] { false, true }) {
                DrawActivationPriority priority = custom ? new(DrawActivationMode.CustomPriority,
                    ["benchmark-secondary", "benchmark-allure"]) : DrawActivationPriority.Default;
                CalculationSnapshot snapshot = CalculationSnapshot.Capture(session.Cards, session.Combos, 5,
                    session.ComboGroups, CalculationWorkPolicy.Interactive, options: new(priority));
                output.Add(new { Name = $"mixed-example-{(vision ? "vision" : "desires")}-{(custom ? "custom" : "configured")}",
                    Input = JsonSerializer.Deserialize<JsonElement>(snapshot.Json),
                    Session = JsonSerializer.Deserialize<JsonElement>(codec.SerializeSession(new() {
                        Cards = session.Cards, Categories = session.Categories, Combos = session.Combos,
                        ComboGroups = session.ComboGroups, HandSize = session.HandSize,
                        CategoryColorIndices = session.CategoryColorIndices, DrawActivationPriority = priority
                    })) });
            }
        }
        File.WriteAllText(Environment.GetEnvironmentVariable("YGO_MIXED_EXAMPLES")!, JsonSerializer.Serialize(output));
    }

    // Freeze these inputs on unchanged dev first. Replay accepts formerly limited
    // outcomes becoming successful, but compares every previously successful row.
    [Test]
    public void Replay() {
        bool wideReference = Environment.GetEnvironmentVariable("YGO_FRONTIER_WIDE_REFERENCE") == "1";
        bool sampleMemory = Environment.GetEnvironmentVariable("YGO_FRONTIER_MEMORY") == "1";
        JsonElement[] fixtures = JsonSerializer.Deserialize<JsonElement[]>(File.ReadAllText(Environment.GetEnvironmentVariable("YGO_AUDIT_INPUT")!))!;
        List<object> output = [];
        foreach (JsonElement fixture in fixtures) {
            string name = fixture.GetProperty("Name").GetString()!;
            CalculationInput input = JsonSerializer.Deserialize(fixture.GetProperty("Input"), CalculationJsonContext.Default.CalculationInput)!;
            List<Card> cards = input.Cards.Select(card => new Card(card.Categories, card.Copies, card.Name, true,
                card.Id, card.ExternalCardId, card.ManualMetadataCategoryKeys, card.DrawCount, card.DrawOncePerTurn,
                card.PostDrawDiscardCount, card.PostDrawSelector, card.PostDrawFailureBehavior, card.PreDrawRemovalCount,
                card.PreDrawSelector, card.PreDrawDeckRemovalCount, card.EffectProfile, card.PostDrawDeckMillCount,
                card.FirstActivationOnly, card.BlocksLaterDraws)).ToList();
            List<Combo> combos = input.Combos.Select(combo => new Combo(combo.Categories, combo.Name, true,
                combo.GroupId, combo.Cards, combo.AlternativeGroups)).ToList();
            DrawActivationPriority? priority = input.Options?.DrawActivationPriority;
            List<double> times = [];
            List<long> allocations = [];
            WorkBudget budget = null!;
            PostDrawCalculation? post = null;
            ExactCalculationResult? exact = null;
            ProbabilityCalculationResult? result = null;
            string outcome = "Success";
            long sampledManagedBytes = 0;
            long sampledResidentBytes = 0;
            for (int trial = 0; trial < 4; trial++) {
                budget = new(new(input.WorkUnits!.Value), CalculationStoragePolicy.ForTier(input.StorageTier ?? CalculationStorageTier.Standard));
                post = null;
                if (cards.Any(card => card.PreDrawRemovalCount > 0 || card.PostDrawDiscardCount > 0)) {
                    post = new(
                        cards,
                        input.HandSize,
                        budget,
                        priority,
                        compactFrontierThreshold: wideReference ? int.MaxValue : PostDrawCalculation.DefaultCompactFrontierThreshold
                    ) {
                        ObserveFrontier = sampleMemory ? ObserveMemory : null
                    };
                }
                long allocated = GC.GetAllocatedBytesForCurrentThread();
                Stopwatch timer = Stopwatch.StartNew();
                exact = null;
                result = null;
                try {
                    exact = post is null ? new DrawEffectCalculation(cards, input.HandSize, budget, priority).Calculate(combos, input.Groups)
                        : post.Calculate(combos, input.Groups);
                    result = exact.ToPublic(combos, input.Groups, budget);
                    outcome = "Success";
                }
                catch (ProbabilityCalculationLimitException error) {
                    outcome = error.Reason.ToString();
                }
                timer.Stop();
                times.Add(timer.Elapsed.TotalMilliseconds);
                allocations.Add(GC.GetAllocatedBytesForCurrentThread() - allocated);
            }

            void ObserveMemory() {
                // Separate diagnostic runs only: full collection changes timings.
                // These are whole-test-process samples at completed layer boundaries,
                // not a per-request allocation count or a total process-memory bound.
                sampledManagedBytes = Math.Max(sampledManagedBytes, GC.GetTotalMemory(true));
                using Process process = Process.GetCurrentProcess();
                sampledResidentBytes = Math.Max(sampledResidentBytes, process.WorkingSet64);
            }

            string[]? fractions = exact is null ? null : new[] { exact.Total }.Concat(exact.Combos).Concat(exact.Groups)
                .Select(value => $"{value.Numerator}/{value.Denominator}").ToArray();
            if (fixture.TryGetProperty("ExactResults", out JsonElement expected) && expected.ValueKind == JsonValueKind.Array) {
                Assert.That(fractions, Is.EqualTo(expected.EnumerateArray().Select(value => value.GetString()).ToArray()), name);
                Assert.That(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(result), fixture.GetProperty("ReferenceResults")), Is.True, name);
            }
            if (result is not null) {
                ProbabilityCalculationResult wire = CalculationWire.ReadResult(CalculationWire.Execute(fixture.GetProperty("Input").GetRawText()));
                Assert.That(JsonSerializer.Serialize(wire), Is.EqualTo(JsonSerializer.Serialize(result)), name);
            }
            output.Add(new { Name = name, Outcome = outcome, Work = budget.Spent,
                WideReference = wideReference, MemorySampling = sampleMemory,
                SampledManagedBytes = sampledManagedBytes, SampledResidentBytes = sampledResidentBytes,
                Milliseconds = times, AllocatedBytes = allocations,
                post?.PackedLayers, post?.PeakConcurrentCells,
                PeakStates = post?.PeakStates ?? 0, PeakCells = post?.PeakCells ?? 0,
                AllStructuresPeakEntries = budget.PeakEntries, AllStructuresPeakCells = budget.PeakCells,
                ReducedRows = post?.ReducedRows ?? 0, DeferredRows = post?.DeferredRows ?? 0,
                ExplicitRows = post is null ? 0 : post.ReducedRows - (post.DeferredRows > 0 ? 1 : 0),
                TerminalStates = post?.TerminalStates ?? 0, post?.LargestStructure,
                Path = DrawExecutionAssessment.Assess(cards, combos, input.HandSize, true).Path.ToString(),
                ExactResults = fractions, Probability = result?.TotalProbability, ReferenceResults = result, Input = fixture.GetProperty("Input"),
                Session = fixture.TryGetProperty("Session", out JsonElement session) ? session : (JsonElement?)null });
            TestContext.Out.WriteLine($"{name}: {outcome}; work={budget.Spent}; rows={post?.ReducedRows}; pool={post?.DeferredRows}; states={post?.PeakStates}; cells={post?.PeakCells}; allCells={budget.PeakCells}; ms={string.Join(',', times)}");
        }
        File.WriteAllText(Environment.GetEnvironmentVariable("YGO_AUDIT_OUTPUT")!, JsonSerializer.Serialize(output));
    }
}
