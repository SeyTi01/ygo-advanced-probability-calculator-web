using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.DrawResolution;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture, Explicit("Isolated anonymous Danger bound research"), NonParallelizable]
public class DangerAnonymousExperiment {
    [Test]
    public void Measure() {
        string directory = Environment.GetEnvironmentVariable("YGO_DANGER_RESEARCH")!;
        string filter = Environment.GetEnvironmentVariable("YGO_DANGER_CASE") ?? "heavy";
        int capacity = int.Parse(Environment.GetEnvironmentVariable("YGO_DANGER_CAPACITY") ?? "16384");
        long work = long.Parse(Environment.GetEnvironmentVariable("YGO_DANGER_WORK") ?? "140000000");
        bool wholeBatches = Environment.GetEnvironmentVariable("YGO_DANGER_BATCHES") == "1";
        bool retainedStratum = Environment.GetEnvironmentVariable("YGO_DANGER_RETAINED_STRATUM") == "1";
        string sourceHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory, "PostDrawDangerAnonymous.cs"))));
        string batchSourceHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(directory, "PostDrawDangerAnonymousBatch.cs"))));
        string assemblyHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(PostDrawCalculation).Assembly.Location)));
        using JsonDocument fixtures = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "frozen-fixtures.json")));
        foreach (JsonElement fixture in fixtures.RootElement.GetProperty("Cases").EnumerateArray()) {
            string name = fixture.GetProperty("Name").GetString()!;
            if (!name.Contains(filter, StringComparison.Ordinal)) {
                continue;
            }
            string inputJson = fixture.GetProperty("Input").GetRawText();
            string inputHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(inputJson)));
            Journal(new { Event = "Started", Name = name, Capacity = capacity, WorkLimit = work, WholeBatches = wholeBatches, RetainedStratum = retainedStratum,
                SourceSha256 = sourceHash, BatchSourceSha256 = batchSourceHash, AssemblySha256 = assemblyHash, InputSha256 = inputHash });
            CalculationInput input = JsonSerializer.Deserialize(inputJson, CalculationJsonContext.Default.CalculationInput)!;
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
            PostDrawCalculation.AnonymousResult result;
            try {
                result = calculation.ResearchAnonymous(combos, input.Groups, capacity, wholeBatches, retainedStratum);
            }
            catch (Exception error) {
                timer.Stop();
                Journal(new { Event = "Failed", Name = name, Capacity = capacity, WorkLimit = work, WholeBatches = wholeBatches, RetainedStratum = retainedStratum,
                    SourceSha256 = sourceHash, BatchSourceSha256 = batchSourceHash, AssemblySha256 = assemblyHash, InputSha256 = inputHash,
                    ErrorType = error.GetType().FullName, Error = error.Message, Work = budget.Spent,
                    Milliseconds = timer.Elapsed.TotalMilliseconds });
                throw;
            }
            timer.Stop();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            BigInteger residual = result.Denominator - result.Resolved;
            string[]? references = fixture.GetProperty("ExactReference").ValueKind == JsonValueKind.Null
                ? null : fixture.GetProperty("ExactReference").EnumerateArray().Select(value => value.GetString()!).ToArray();
            bool[] contained = result.Wins.Select((win, row) => {
                if (references is null) {
                    return true;
                }
                string[] fraction = references[row].Split('/');
                BigInteger numerator = BigInteger.Parse(fraction[0]);
                BigInteger referenceDenominator = BigInteger.Parse(fraction[1]);
                return win * referenceDenominator <= numerator * result.Denominator
                    && numerator * result.Denominator <= (win + residual) * referenceDenominator;
            }).ToArray();
            object sample = new { Event = "Sample", Name = name, Capacity = capacity, WorkLimit = work, Work = budget.Spent,
                WholeBatches = wholeBatches, RetainedStratum = retainedStratum,
                SourceSha256 = sourceHash, BatchSourceSha256 = batchSourceHash, AssemblySha256 = assemblyHash, InputSha256 = inputHash,
                result.FinalizationWork, budget.PeakEntries, budget.PeakCells, calculation.PeakConcurrentCells,
                result.Expanded, result.Evicted, result.Reveals, result.TerminalSplits, result.TerminalHits,
                result.SkippedReveals, result.RetainedDiscards, result.RetainedReveals,
                result.JointStratumReveals, result.EmptyRetainedReveals, result.RetainedObserverReveals,
                result.PriorityWork, result.ScaleWork, result.Stop, Milliseconds = timer.Elapsed.TotalMilliseconds,
                AllocatedBytes = allocated, Denominator = result.Denominator.ToString(), Resolved = result.Resolved.ToString(),
                WidthPercentagePoints = 100 * (double)residual / (double)result.Denominator,
                Rows = result.Wins.Select((win, row) => new { Lower = win.ToString(), Upper = (win + residual).ToString(),
                    ReferenceContained = references is null ? (bool?)null : contained[row] }).ToArray(),
                ReferenceChecked = references is not null };
            Journal(sample);
            Assert.That(residual.Sign, Is.GreaterThanOrEqualTo(0));
            Assert.That(result.Wins.All(win => win >= 0 && win <= result.Resolved), Is.True);
            Assert.That(contained.All(value => value), Is.True, $"{name}: all frozen row references enclosed");
        }

        void Journal(object value) {
            string json = JsonSerializer.Serialize(value);
            File.AppendAllText(Path.Combine(directory, "anonymous-results.jsonl"), json + Environment.NewLine);
            TestContext.Out.WriteLine(json);
        }
    }
}
