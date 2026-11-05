using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using CardProbability.EngineTests.Fixtures;
using CardProbability.Internal;
using CardProbability.Internal.Contracts;
using CardProbability.Internal.DrawResolution;
using CardProbability.Internal.Model;
using CardProbability.Internal.Resources;
using CardProbability.Internal.Search;

namespace CardProbability.EngineTests;

// The input is a comma-separated list of owned fixture names, not app wire JSON.
// Output is diagnostic data, not a timing benchmark or a serialization contract.
[TestFixture, Explicit("Untimed ordered phase/lifetime profile; never a timing benchmark"), NonParallelizable]
internal sealed class OrderedThroughputProfile {
    [Test]
    public void Profile() {
        string[] names = (Environment.GetEnvironmentVariable("CARD_PROBABILITY_ORDERED_PROFILE_FIXTURES")
            ?? "hidden-deck-cost,hidden-deck-mill")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string? outputPath = Environment.GetEnvironmentVariable("CARD_PROBABILITY_ORDERED_PROFILE_OUTPUT");
        List<object> output = [];
        foreach (string name in names) {
            EngineCase fixture = NeutralPressureFixtures.Load(name);
            SearchCountingDiagnostics diagnostics = new();
            WorkBudget budget = new(new(fixture.Options.Policy.WorkUnits ?? 150_000_000)) { SearchDiagnostics = diagnostics };
            PreparedCalculation input = fixture.Prepare(budget);
            DrawActivationPriority priority = fixture.Options.ActivationOrder == ActivationOrder.Priority
                ? new(DrawActivationMode.CustomPriority, fixture.Options.PriorityEntryIds)
                : DrawActivationPriority.Default;
            long heap = 0;
            long workingSet = 0;
            long terminals = 0;
            BigInteger mass = 0;
            PostDrawCalculation calculation = new(input.Cards, fixture.Request.HandSize, budget, priority) {
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
                calculation.CalculateSearch(input.Combos, input.Groups, false, new SearchAccessPlan(input.Cards, budget));
                Record();
                phase = "witness";
                calculation.CalculateSearchLower(input.Combos, input.Groups, true);
                Record();
                phase = "lower";
                calculation.CalculateSearchLower(input.Combos, input.Groups, false);
                Record();
            }
            catch (ProbabilityCalculationLimitException error) {
                outcome = error.Reason + "Limit";
            }
            output.Add(new { Name = name, Outcome = outcome, Phase = phase, Passes = passes,
                Work = budget.Spent, Entries = budget.PeakEntries, Cells = budget.PeakCells, calculation.PeakConcurrentCells,
                calculation.LargestStructure, calculation.ReducedRows, SampledLiveHeap = heap, SampledWorkingSet = workingSet,
                TerminalContributions = terminals, TerminalMass = mass.ToString(), diagnostics.Counts, diagnostics.Milliseconds });
            string json = JsonSerializer.Serialize(output);
            if (outputPath is null) {
                TestContext.Out.WriteLine(json);
            }
            else {
                File.WriteAllText(outputPath, json);
            }

            void Record() => passes.Add(new { Phase = phase, Work = budget.Spent, TerminalContributions = terminals,
                TerminalMass = mass.ToString(), Heap = GC.GetTotalMemory(true), WorkingSet = Process.GetCurrentProcess().WorkingSet64 });
        }
    }
}
