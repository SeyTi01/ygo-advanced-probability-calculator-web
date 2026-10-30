using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.DrawResolution;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture, Explicit("Isolated Danger bound research; never included in normal builds"), NonParallelizable]
public class DangerContinuationExperiment {
    [Test]
    public void Measure() {
        string directory = Environment.GetEnvironmentVariable("YGO_DANGER_RESEARCH")!;
        string filter = Environment.GetEnvironmentVariable("YGO_DANGER_CASE") ?? "heavy";
        int capacity = int.Parse(Environment.GetEnvironmentVariable("YGO_DANGER_CAPACITY") ?? "4000");
        int horizon = int.Parse(Environment.GetEnvironmentVariable("YGO_DANGER_HORIZON") ?? "8");
        long work = long.Parse(Environment.GetEnvironmentVariable("YGO_DANGER_WORK") ?? "140000000");
        using JsonDocument fixtures = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "frozen-fixtures.json")));
        foreach (JsonElement fixture in fixtures.RootElement.GetProperty("Cases").EnumerateArray()) {
            string name = fixture.GetProperty("Name").GetString()!;
            if (!name.Contains(filter, StringComparison.Ordinal)) {
                continue;
            }
            CalculationInput input = JsonSerializer.Deserialize(fixture.GetProperty("Input").GetRawText(), CalculationJsonContext.Default.CalculationInput)!;
            List<Card> cards = input.Cards.Select(card => new Card(card.Categories, card.Copies, card.Name, true,
                card.Id, card.ExternalCardId, card.ManualMetadataCategoryKeys, card.DrawCount, card.DrawOncePerTurn,
                card.PostDrawDiscardCount, card.PostDrawSelector, card.PostDrawFailureBehavior, card.PreDrawRemovalCount,
                card.PreDrawSelector, card.PreDrawDeckRemovalCount, card.EffectProfile, card.PostDrawDeckMillCount,
                card.FirstActivationOnly, card.BlocksLaterDraws, card.Search, card.OncePerTurn, card.ActivationLimit,
                card.MinimumHandSize, drawOperation: card.DrawOperation)).ToList();
            List<Combo> combos = input.Combos.Select(combo => new Combo(combo.Categories, combo.Name, true,
                combo.GroupId, combo.Cards, combo.AlternativeGroups)).ToList();
            WorkBudget budget = new(new(work));
            PostDrawCalculation calculation = new(cards, input.HandSize, budget, input.Options?.DrawActivationPriority);
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            Stopwatch timer = Stopwatch.StartNew();
            PostDrawCalculation.ContinuationResult result = calculation.ResearchContinuation(combos, input.Groups, capacity, horizon);
            timer.Stop();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            string[]? references = fixture.GetProperty("ExactReference").ValueKind == JsonValueKind.Null
                ? null : fixture.GetProperty("ExactReference").EnumerateArray().Select(value => value.GetString()!).ToArray();
            for (int row = 0; row < result.Lower.Length; row++) {
                Assert.That(result.Lower[row] <= result.Upper[row] && result.Upper[row] <= result.Scale, Is.True);
                if (references is not null) {
                    string[] fraction = references[row].Split('/');
                    BigInteger numerator = BigInteger.Parse(fraction[0]);
                    BigInteger denominator = BigInteger.Parse(fraction[1]);
                    Assert.That(result.Lower[row] * denominator <= numerator * result.Scale
                        && numerator * result.Scale <= result.Upper[row] * denominator,
                        Is.True, $"{name} row {row} containment");
                }
            }
            object sample = new { Name = name, Capacity = capacity, Horizon = horizon, WorkLimit = work, Work = budget.Spent,
                result.FinalizationWork, budget.PeakEntries, budget.PeakCells, calculation.PeakConcurrentCells,
                result.Expanded, result.Hits, result.Evicted, result.CacheCells, result.Stop, Milliseconds = timer.Elapsed.TotalMilliseconds,
                AllocatedBytes = allocated, Denominator = result.Scale.ToString(),
                Rows = result.Lower.Select((value, row) => new { Lower = value.ToString(), Upper = result.Upper[row].ToString(),
                    WidthPercentagePoints = 100.0 * (result.Upper[row] - value) / result.Scale }).ToArray(),
                ReferenceChecked = references is not null };
            string json = JsonSerializer.Serialize(sample);
            File.AppendAllText(Path.Combine(directory, "continuation-results.jsonl"), json + Environment.NewLine);
            TestContext.Out.WriteLine(json);
        }
    }
}

