using System.Diagnostics;
using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

// Replay frozen worker inputs without regenerating expectations from the candidate.
// Explicit and serial; run with DOTNET_TieredCompilation=0, away from other jobs.
[TestFixture, Explicit("Frozen-input performance audit"), NonParallelizable]
public class PerformanceAuditBenchmark {
    [Test]
    public void Replay() {
        string inputPath = Environment.GetEnvironmentVariable("YGO_AUDIT_INPUT")!;
        string outputPath = Environment.GetEnvironmentVariable("YGO_AUDIT_OUTPUT")!;
        int trials = int.Parse(Environment.GetEnvironmentVariable("YGO_AUDIT_TRIALS") ?? "7");
        Assert.That(trials, Is.InRange(1, 30));
        JsonElement[] fixtures = JsonSerializer.Deserialize<JsonElement[]>(File.ReadAllText(inputPath))!;
        List<object> measurements = [];
        foreach (JsonElement fixture in fixtures) {
            string name = fixture.GetProperty("Name").GetString()!;
            CalculationInput input = JsonSerializer.Deserialize(fixture.GetProperty("Input"), CalculationJsonContext.Default.CalculationInput)!;
            List<Card> cards = input.Cards.Select(card => new Card(
                card.Categories,
                card.Copies,
                card.Name,
                true,
                card.Id,
                card.ExternalCardId,
                card.ManualMetadataCategoryKeys,
                card.DrawCount,
                card.DrawOncePerTurn,
                card.PostDrawDiscardCount,
                card.PostDrawSelector,
                card.PostDrawFailureBehavior,
                card.PreDrawRemovalCount,
                card.PreDrawSelector,
                card.PreDrawDeckRemovalCount,
                card.EffectProfile,
                card.PostDrawDeckMillCount,
                card.FirstActivationOnly,
                card.BlocksLaterDraws
            )).ToList();
            List<Combo> combos = input.Combos.Select(combo => new Combo(
                combo.Categories,
                combo.Name,
                true,
                combo.GroupId,
                combo.Cards,
                combo.AlternativeGroups
            )).ToList();
            for (int trial = -1; trial < trials; trial++) {
                WorkBudget budget = new(new(input.WorkUnits!.Value), CalculationStoragePolicy.ForTier(input.StorageTier ?? CalculationStorageTier.Standard));
                PostDrawCalculation? conditional = cards.Any(card => card.PreDrawRemovalCount > 0 || card.PostDrawDiscardCount > 0)
                    ? new(cards, input.HandSize, budget, input.Options?.DrawActivationPriority) : null;
                long allocated = GC.GetAllocatedBytesForCurrentThread();
                Stopwatch timer = Stopwatch.StartNew();
                ExactCalculationResult? exact = null;
                ProbabilityCalculationResult? result = null;
                string outcome = "Success";
                try {
                    exact = conditional is null
                        ? new DrawEffectCalculation(cards, input.HandSize, budget, input.Options?.DrawActivationPriority).Calculate(combos, input.Groups)
                        : conditional.Calculate(combos, input.Groups);
                    result = exact.ToPublic(combos, input.Groups, budget);
                }
                catch (ProbabilityCalculationLimitException error) {
                    outcome = error.Reason.ToString();
                }
                timer.Stop();
                allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
                if (fixture.TryGetProperty("Outcome", out JsonElement expectedOutcome)) {
                    Assert.That(outcome, Is.EqualTo(expectedOutcome.GetString()), name);
                }
                string[]? fractions = exact is null ? null : new[] { exact.Total }.Concat(exact.Combos).Concat(exact.Groups)
                    .Select(value => $"{value.Numerator}/{value.Denominator}").ToArray();
                if (fixture.TryGetProperty("ExactResults", out JsonElement expected) && expected.ValueKind == JsonValueKind.Array) {
                    string[] values = expected.EnumerateArray().Select(value => value.GetString()!).ToArray();
                    Assert.That(fractions, Is.EqualTo(values), name);
                }
                if (fixture.TryGetProperty("ReferenceResults", out JsonElement reference) && reference.ValueKind == JsonValueKind.Object) {
                    Assert.That(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(result), reference), Is.True, name);
                }
                if (trial < 0) {
                    if (result is not null) {
                        ProbabilityCalculationResult wire = CalculationWire.ReadResult(CalculationWire.Execute(fixture.GetProperty("Input").GetRawText()));
                        Assert.That(JsonSerializer.Serialize(wire), Is.EqualTo(JsonSerializer.Serialize(result)), name);
                    }
                    continue;
                }
                measurements.Add(new { Name = name, Trial = trial, Outcome = outcome, Work = budget.Spent,
                    Milliseconds = timer.Elapsed.TotalMilliseconds, AllocatedBytes = allocated,
                    conditional?.TerminalStates, conditional?.PeakStates, conditional?.PeakCells, conditional?.LargestStructure,
                    conditional?.ReducedRows, ExactResults = fractions, Probability = result?.TotalProbability,
                    ReferenceResults = result, Input = fixture.GetProperty("Input"),
                    // End-of-request observations, not peak live memory or per-request bytes.
                    ManagedHeapBytes = GC.GetTotalMemory(false), ProcessWorkingSetBytes = Process.GetCurrentProcess().WorkingSet64 });
            }
            TestContext.Out.WriteLine(name);
        }
        File.WriteAllText(outputPath, JsonSerializer.Serialize(measurements));
    }
}
