using System.Diagnostics;
using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

// Copy this unchanged file into the frozen baseline test project. Run the two
// Release builds serially with DOTNET_TieredCompilation=0 and identical inputs.
// YGO_UNIFIED_BENCHMARK is the output path; YGO_UNIFIED_REVISION identifies the
// measured source. CASE filters a substring; CASE_EXACT selects one full name.
// EXPORT_ONLY writes pending inputs for the external process-timeout harness.
[TestFixture, Explicit("Unified activation native/WASM comparison"), NonParallelizable]
public sealed class UnifiedActivationBenchmark {
    private sealed record Fixture(string Name, bool PreserveBaseline, SessionState State);
    private sealed record Measurement(
        double Milliseconds,
        long AllocatedBytes,
        long ManagedHeapBytes,
        long WorkingSetBytes,
        string Outcome,
        long Work,
        long Entries,
        long Cells,
        ProbabilityCalculationResult? Result
    );

    [Test]
    public async Task Measure() {
        string output = Environment.GetEnvironmentVariable("YGO_UNIFIED_BENCHMARK")
            ?? throw new InvalidOperationException("Set YGO_UNIFIED_BENCHMARK to the output JSON path.");
        string revision = Environment.GetEnvironmentVariable("YGO_UNIFIED_REVISION")
            ?? throw new InvalidOperationException("Set YGO_UNIFIED_REVISION to the measured source revision.");
        string? selected = Environment.GetEnvironmentVariable("YGO_UNIFIED_CASE");
        string? exactName = Environment.GetEnvironmentVariable("YGO_UNIFIED_CASE_EXACT");
        bool exportOnly = Environment.GetEnvironmentVariable("YGO_UNIFIED_EXPORT_ONLY") == "1";
        int samples = int.TryParse(Environment.GetEnvironmentVariable("YGO_UNIFIED_SAMPLES"), out int count) ? count : 3;
        Assert.That(samples, Is.GreaterThan(0));
        List<object> results = [];
        // Keep the supplied session intact, including its encounter ordering,
        // distinct entries and effects. Source() verifies the frozen file hash.
        SessionState supplied = await SelectiveSurvivorTest.Source();
        List<Fixture> fixtures = [.. Fixtures(), new("supplied-desires-trade-in-40", false, supplied)];
        foreach (int copies in new[] { 1, 3 }) {
            SessionState example = await SearchEffectsTest.LoadExample("rota_search_example_session_state.json");
            Assert.That(example.DrawActivationPriority.Mode, Is.EqualTo(DrawActivationMode.EncounterOrder));
            int row = example.Cards.FindIndex(card => card.Name == "Mulcharmy Fuwalos");
            Card original = example.Cards[row];
            example.Cards[row] = original.WithCopies(copies).WithDrawCount(2).WithDrawOncePerTurn(false);
            if (copies < original.Copies) {
                example.Cards.Add(new Card(original.Categories, original.Copies - copies, original.Name,
                    original.Active, "benchmark-inert-fuwalos", original.ExternalCardId, original.ManualMetadataCategoryKeys,
                    simulationEnabled: original.SimulationEnabled).WithEffect(original.EffectPlan, original.EffectProfile));
            }
            Assert.That(example.Cards.Sum(card => card.Copies), Is.EqualTo(40));
            fixtures.Add(new("rota-encounter-draw2-copies" + copies + "-40", false, example));
        }
        foreach (Fixture fixture in fixtures) {
            foreach (CalculationModel mode in new[] { CalculationModel.Fast, CalculationModel.Exact }) {
                string name = fixture.Name + "-" + mode;
                if (exactName is not null ? name != exactName
                    : selected is not null && !name.Contains(selected, StringComparison.Ordinal)) {
                    continue;
                }
                SessionState state = fixture.State;
                CalculationOptions options = new(state.DrawActivationPriority, Mode: mode);
                CalculationSnapshot snapshot = CalculationSnapshot.Capture(state.Cards, state.Combos, state.HandSize,
                    state.ComboGroups, CalculationWorkPolicy.Interactive, storagePolicy: CalculationStoragePolicy.Standard, options: options);
                List<Measurement> measured = [];
                results.Add(Entry(new { Outcome = "Pending" }));
                File.WriteAllText(output, JsonSerializer.Serialize(results));
                if (exportOnly) {
                    continue;
                }
                TestContext.Progress.WriteLine("Measuring " + name);
                Measurement first = Run();
                Checkpoint();
                for (int trial = 0; trial < samples; trial++) {
                    Measurement value = Run();
                    Assert.That(value.Outcome, Is.EqualTo(first.Outcome), name);
                    Assert.That(value.Work, Is.EqualTo(first.Work), name);
                    Assert.That(value.Entries, Is.EqualTo(first.Entries), name);
                    Assert.That(value.Cells, Is.EqualTo(first.Cells), name);
                    Assert.That(JsonSerializer.Serialize(value.Result), Is.EqualTo(JsonSerializer.Serialize(first.Result)), name);
                    measured.Add(value);
                    Checkpoint();
                }
                TestContext.Out.WriteLine($"{name}: {first.Outcome}; route={first.Result?.Contract?.SolverRoute}; "
                    + $"median={measured.Select(value => value.Milliseconds).Order().ElementAt(samples / 2):F3}ms; "
                    + $"work={first.Work}; entries={first.Entries}; cells={first.Cells}");

                object Entry(object firstMeasurement) => new {
                    Name = name,
                    fixture.PreserveBaseline,
                    SourceRevision = revision,
                    Cards = state.Cards.Sum(card => card.Copies),
                    Entries = state.Cards.Count,
                    state.HandSize,
                    Input = JsonSerializer.Deserialize<JsonElement>(snapshot.Json),
                    First = firstMeasurement,
                    Samples = measured
                };

                void Checkpoint() {
                    results[^1] = Entry(first);
                    File.WriteAllText(output, JsonSerializer.Serialize(results));
                }

                Measurement Run() {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    WorkBudget budget = new(mode == CalculationModel.Exact ? CalculationWorkPolicy.Unlimited : CalculationWorkPolicy.Interactive);
                    long allocated = GC.GetAllocatedBytesForCurrentThread();
                    long started = Stopwatch.GetTimestamp();
                    string outcome = "Success";
                    ProbabilityCalculationResult? result = null;
                    try {
                        result = new ProbabilityCalculatorService().CalculateWithBudget(state.Cards, state.Combos,
                            state.HandSize, state.ComboGroups, budget, options);
                    }
                    catch (ProbabilityCalculationLimitException exception) {
                        outcome = exception.Reason + "Limit";
                    }
                    catch (UnsupportedExactCalculationException) {
                        outcome = "Unsupported";
                    }
                    double milliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
                    return new(milliseconds, allocated, GC.GetTotalMemory(false), Process.GetCurrentProcess().WorkingSet64,
                        outcome, budget.Spent, budget.PeakEntries, budget.PeakCells, result);
                }
            }
        }
        Assert.That(results, Is.Not.Empty, "The case filter must select at least one fixture.");
    }

    private static IEnumerable<Fixture> Fixtures() {
        CategoryBase a = new("A");
        CategoryBase b = new("B");
        CategoryBase material = new("Material");
        CardSelector targets = CardSelector.AnyOfSelectors([CardSelector.For(a), CardSelector.For(b)]);
        foreach (int size in new[] { 40, 60 }) {
            foreach (string kind in new[] { "draw-only", "search-only", "mixed-draw-first", "mixed-search-first", "mixed-encounter" }) {
                List<Card> cards = [new([a], 3, "Target A", id: "a"), new([b], 3, "Target B", id: "b")];
                if (kind != "search-only") {
                    cards.Add(new([], 3, "Draw one", id: "draw", drawCount: 1));
                }
                if (kind != "draw-only") {
                    cards.Add(new([], 3, "Search A or B", id: "search", search: new(targets, false), oncePerTurn: true));
                }
                Fill(cards, size, false);
                DrawActivationPriority priority = kind == "mixed-encounter" ? DrawActivationPriority.Default
                    : new(DrawActivationMode.CustomPriority, kind == "mixed-search-first" ? ["search", "draw"] : ["draw", "search"]);
                yield return new(kind + "-" + size, kind is "draw-only" or "search-only" or "mixed-draw-first", State(cards, priority));
            }
            List<Card> distinct = [new([a], 3, "Target A", id: "a"), new([b], 3, "Target B", id: "b"),
                new([], 3, "Draw one", id: "draw", drawCount: 1),
                new([], 3, "Search A or B", id: "search", search: new(targets, false), oncePerTurn: true)];
            Fill(distinct, size, true);
            yield return new("mixed-search-first-distinct-" + size, false,
                State(distinct, new(DrawActivationMode.CustomPriority, ["search", "draw"])));
        }
        List<Card> restricted = [new([a], 3, "Target A", id: "a"), new([b], 3, "Target B", id: "b"),
            new([material], 3, "Search payment", id: "material"),
            new([], 3, "Pay then search A", id: "paid", search: new(CardSelector.For(a), false),
                preDrawRemovalCount: 1, preDrawSelector: CardSelector.For(material)),
            new([], 3, "Search B with five cards", id: "minimum", search: new(CardSelector.For(b), false), minimumHandSize: 5)];
        Fill(restricted, 40, false);
        foreach (string first in new[] { "paid", "minimum" }) {
            yield return new("search-restricted-" + first + "-40", false,
                State(restricted, new(DrawActivationMode.CustomPriority, first == "paid" ? ["paid", "minimum"] : ["minimum", "paid"])));
        }
        List<Card> hidden = [new([a], 3, "Target A", id: "a"), new([b], 3, "Target B", id: "b"),
            new([], 1, "Banish two then draw", id: "draw", drawCount: 1, preDrawDeckRemovalCount: 2),
            new([], 2, "Search A or B", id: "search", search: new(targets, false))];
        Fill(hidden, 40, false);
        yield return new("mixed-hidden-40", false, State(hidden, DrawActivationPriority.Default));

        SessionState State(List<Card> cards, DrawActivationPriority priority) => new() {
            Cards = cards,
            HandSize = 5,
            DrawActivationPriority = priority,
            Categories = [a, b, material],
            Combos = [
                new([new(a, 1, 5), new(b, 1, 5)], "A plus B", groupId: "g"),
                new([new(a, 2, 5)], "Two A", groupId: "g"),
                new([new(b, 2, 5)], "Two B")
            ],
            ComboGroups = [new("g", "A routes")]
        };
    }

    private static void Fill(List<Card> cards, int size, bool distinct) {
        int left = size - cards.Sum(card => card.Copies);
        for (int index = 0; left > 0; index++) {
            int copies = distinct ? Math.Min(3, left) : left;
            cards.Add(new([], copies, distinct ? "Other " + index : "Equivalent other cards", id: "other-" + index));
            left -= copies;
        }
    }
}
