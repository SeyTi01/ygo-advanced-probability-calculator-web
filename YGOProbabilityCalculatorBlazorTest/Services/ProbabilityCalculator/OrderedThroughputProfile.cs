using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture, Explicit("Untimed ordered phase/lifetime profile; never a timing benchmark"), NonParallelizable]
public sealed class OrderedThroughputProfile {
    [Test]
    public void Profile() {
        string inputPath = Environment.GetEnvironmentVariable("YGO_ORDERED_PROFILE_INPUT")!;
        string outputPath = Environment.GetEnvironmentVariable("YGO_ORDERED_PROFILE_OUTPUT")!;
        List<object> output = [];
        foreach (JsonElement fixture in JsonSerializer.Deserialize<JsonElement[]>(File.ReadAllText(inputPath))!) {
            CalculationInput input = fixture.GetProperty("Input").Deserialize(CalculationJsonContext.Default.CalculationInput)!;
            List<Card> cards = input.Cards.Select(card => new Card(card.Categories, card.Copies, card.Name, true,
                card.Id, card.ExternalCardId, card.ManualMetadataCategoryKeys, card.DrawCount, card.DrawOncePerTurn,
                card.PostDrawDiscardCount, card.PostDrawSelector, card.PostDrawFailureBehavior, card.PreDrawRemovalCount,
                card.PreDrawSelector, card.PreDrawDeckRemovalCount, card.EffectProfile, card.PostDrawDeckMillCount,
                card.FirstActivationOnly, card.BlocksLaterDraws, card.Search, card.OncePerTurn)).ToList();
            List<Combo> combos = input.Combos.Select(combo => new Combo(combo.Categories, combo.Name, true,
                combo.GroupId, combo.Cards, combo.AlternativeGroups)).ToList();
            SearchCountingDiagnostics diagnostics = new();
            WorkBudget budget = new(new(input.WorkUnits ?? 150_000_000), CalculationStoragePolicy.ForTier(input.StorageTier ?? CalculationStorageTier.Standard)) {
                SearchDiagnostics = diagnostics
            };
            long heap = 0;
            long workingSet = 0;
            long terminals = 0;
            BigInteger mass = 0;
            PostDrawCalculation calculation = new(cards, input.HandSize, budget, input.Options?.DrawActivationPriority) {
                ObserveFrontier = () => {
                    heap = Math.Max(heap, GC.GetTotalMemory(true));
                    workingSet = Math.Max(workingSet, Process.GetCurrentProcess().WorkingSet64);
                },
                ObserveTerminal = (_, weight) => { terminals++; mass += weight; }
            };
            string outcome = "Success";
            string phase = "primary";
            List<object> passes = [];
            try {
                calculation.CalculateSearch(combos, input.Groups, false, new(cards, budget));
                Record();
                phase = "witness";
                calculation.CalculateSearchLower(combos, input.Groups, true);
                Record();
                phase = "lower";
                calculation.CalculateSearchLower(combos, input.Groups, false);
                Record();
            }
            catch (ProbabilityCalculationLimitException error) {
                outcome = error.Reason + "Limit";
            }
            output.Add(new { Name = fixture.GetProperty("Name").GetString(), Outcome = outcome, Phase = phase, Passes = passes,
                Work = budget.Spent, Entries = budget.PeakEntries, Cells = budget.PeakCells, calculation.PeakConcurrentCells,
                calculation.LargestStructure, calculation.ReducedRows, SampledLiveHeap = heap, SampledWorkingSet = workingSet,
                TerminalContributions = terminals, TerminalMass = mass.ToString(), diagnostics.Counts, diagnostics.Milliseconds });
            File.WriteAllText(outputPath, JsonSerializer.Serialize(output));

            void Record() => passes.Add(new { Phase = phase, Work = budget.Spent, TerminalContributions = terminals,
                TerminalMass = mass.ToString(), Heap = GC.GetTotalMemory(true), WorkingSet = Process.GetCurrentProcess().WorkingSet64 });
        }
    }
}
