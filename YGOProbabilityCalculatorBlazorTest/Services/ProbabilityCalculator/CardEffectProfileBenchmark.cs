using YGOProbabilityCalculatorBlazorTest.Services.CardEffects;
using System.Diagnostics;
using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;
using YGOProbabilityCalculatorBlazor.Services.DeckImport;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.DrawResolution;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture, Explicit("Card profile parity and performance matrix"), NonParallelizable]
public class CardEffectProfileBenchmark {
    [Test]
    public void Measure() {
        CategoryBase starter = new("Starter");
        CategoryBase extender = new("Extender");
        CategoryBase dark = new("DARK", CategorySource.Metadata, "attribute:dark");
        CategoryBase level = new("Level 8", CategorySource.Metadata, "level:8");
        CategoryBase monster = new("Monster", CategorySource.Metadata, "kind:monster");
        CategoryBase orcust = new("Orcust", CategorySource.Metadata, "archetype:orcust");
        List<Combo> combos = [new([new(starter, 1, 5), new(extender, 1, 5)], groupId: "g"),
            new([ComboCategory.SameCard([starter, extender], 1, 5)], groupId: "g")];
        List<ComboGroup> groups = [new("g", "Routes"), new("empty", "Empty")];
        List<object> output = [];
        foreach (string name in new[] { "control", "desires", "desires-allure", "three-graceful", "trade-in", "return" }) {
            List<Card> effects = name switch {
                "control" => [],
                "desires" => [Effect(35261759, 3)],
                "desires-allure" => [Effect(35261759, 3), Effect(1475311, 2)],
                "three-graceful" => [Effect(79571449, 3)],
                "trade-in" => [Effect(38120068, 2)],
                _ => [Effect(26845680, 2)]
            };
            List<Card> cards = [new([starter, dark, level, monster, orcust], 6, id: "a"),
                new([extender], 6, id: "b"), new([starter, extender, dark, monster, orcust], 3, id: "ab"),
                new([], 25 - effects.Sum(card => card.Copies), id: "filler"), .. effects];
            List<double> times = [];
            List<long> allocations = [];
            List<double> captureTimes = [];
            List<long> captureAllocations = [];
            long work = 0;
            int peakStates = 0;
            long peakCells = 0;
            ExactCalculationResult? exact = null;
            ProbabilityCalculationResult? result = null;
            string outcome = "Success";
            CalculationSnapshot? snapshot = null;
            for (int trial = -1; trial < 3; trial++) {
                WorkBudget budget = new(CalculationWorkPolicy.Interactive);
                PostDrawCalculation? post = cards.Any(card => card.PreDrawRemovalCount > 0 || card.PostDrawDiscardCount > 0)
                    ? new(cards, 5, budget) : null;
                long start = GC.GetAllocatedBytesForCurrentThread();
                Stopwatch timer = Stopwatch.StartNew();
                try {
                    exact = post is null ? new DrawEffectCalculation(cards, 5, budget).Calculate(combos, groups)
                        : post.Calculate(combos, groups);
                    result = exact.ToPublic(combos, groups, budget);
                }
                catch (ProbabilityCalculationLimitException error) {
                    outcome = error.Reason.ToString();
                }
                timer.Stop();
                long allocated = GC.GetAllocatedBytesForCurrentThread() - start;
                if (trial >= 0) {
                    times.Add(timer.Elapsed.TotalMilliseconds);
                    allocations.Add(allocated);
                }
                start = GC.GetAllocatedBytesForCurrentThread();
                timer.Restart();
                snapshot = CalculationSnapshot.Capture(cards, combos, 5, groups, CalculationWorkPolicy.Interactive, options: new());
                timer.Stop();
                if (trial >= 0) {
                    captureTimes.Add(timer.Elapsed.TotalMilliseconds);
                    captureAllocations.Add(GC.GetAllocatedBytesForCurrentThread() - start);
                }
                work = budget.Spent;
                peakStates = post?.PeakStates ?? 0;
                peakCells = post?.PeakCells ?? 0;
            }
            if (outcome == "Success") {
                Assert.That(JsonSerializer.Serialize(CalculationWire.ReadResult(CalculationWire.Execute(snapshot!.Json))),
                    Is.EqualTo(JsonSerializer.Serialize(result)));
            }
            output.Add(new { Name = name, Outcome = outcome, Work = work, PeakStates = peakStates, PeakCells = peakCells,
                Milliseconds = times, AllocatedBytes = allocations, CaptureMilliseconds = captureTimes, CaptureAllocatedBytes = captureAllocations,
                Path = DrawExecutionAssessment.Assess(cards, combos, 5, true).Path.ToString(),
                Risk = DrawExecutionAssessment.Assess(cards, combos, 5, true).ElevatedRisk,
                Probability = result?.TotalProbability, ReferenceResults = result,
                ExactResults = exact is null ? null : new[] { exact.Total }.Concat(exact.Combos).Concat(exact.Groups)
                    .Select(value => $"{value.Numerator}/{value.Denominator}").ToArray(),
                Input = JsonSerializer.Deserialize<JsonElement>(snapshot!.Json) });
            TestContext.Out.WriteLine($"{name}: {outcome}, work={work}, states={peakStates}, cells={peakCells}, ms={string.Join(',', times)}");
        }
        File.WriteAllText(Environment.GetEnvironmentVariable("YGO_PROFILE_BENCHMARK_OUTPUT")
            ?? throw new InvalidOperationException("Set YGO_PROFILE_BENCHMARK_OUTPUT."), JsonSerializer.Serialize(output));
    }

    // Opt-in measurements use the shipped Fast allowance and unlimited interactive
    // Exact, both at Standard Storage. A resource failure is evidence, not a reason
    // to promote a tier or alter the fixed activation policy.
    [Test]
    public void MeasureDanger() {
        string outputPath = Environment.GetEnvironmentVariable("YGO_DANGER_BENCHMARK_OUTPUT")
            ?? throw new InvalidOperationException("Set YGO_DANGER_BENCHMARK_OUTPUT.");
        bool exportOnly = Environment.GetEnvironmentVariable("YGO_DANGER_BENCHMARK_EXPORT_ONLY") == "1";
        int trials = int.TryParse(Environment.GetEnvironmentVariable("YGO_DANGER_BENCHMARK_TRIALS"), out int requestedTrials)
            ? Math.Clamp(requestedTrials, 1, 10) : 3;
        string? caseFilter = Environment.GetEnvironmentVariable("YGO_DANGER_BENCHMARK_CASE");
        CategoryBase starter = new("Starter");
        CategoryBase extender = new("Extender");
        CategoryBase dark = new("DARK", CategorySource.Metadata, "attribute:dark");
        List<Combo> combos = [
            new([new(starter, 1, 5), new(extender, 1, 5)], "Starter and extender", groupId: "g"),
            new([ComboCategory.SameCard([starter, extender], 1, 5)], "Overlap", groupId: "g"),
            new([new(starter, 0, 0), new(extender, 1, 5)], "Extender without starter", groupId: "h")
        ];
        List<ComboGroup> groups = [new("g", "Positive routes"), new("h", "Exclusion route")];
        List<object> output = [];
        foreach (int size in new[] { 40, 60 }) {
            foreach (string shape in new[] { "control", "single", "mixed", "heavy" }) {
                List<Card> effects = shape switch {
                    "control" => [Preset(70368879, 1)],
                    "single" => [Preset(43316238, 1)],
                    "mixed" => [Preset(43316238, 1), Preset(70711847, 1), Preset(70368879, 1),
                        new([], 1, "Search starter", id: "search", search: new(CardSelector.For(starter), false))],
                    _ => [Preset(43316238, 3), Preset(70711847, 3), Preset(52350806, 2),
                        Preset(1475311, 2), Preset(79571449, 1),
                        new([], 2, "Search starter", id: "search", search: new(CardSelector.For(starter), false))]
                };
                List<Card> cards = [new([starter, dark], 6, id: "starter"), new([extender], 6, id: "extender"),
                    new([starter, extender, dark], 3, id: "overlap"),
                    new([], size - 15 - effects.Sum(card => card.Copies), id: "filler"), .. effects];
                foreach (CalculationModel mode in new[] { CalculationModel.Fast, CalculationModel.Exact }) {
                    string name = $"danger-{shape}-{size}-{mode.ToString().ToLowerInvariant()}";
                    if (caseFilter is not null && !name.Contains(caseFilter, StringComparison.OrdinalIgnoreCase)) {
                        continue;
                    }
                    TestContext.Progress.WriteLine($"{(exportOnly ? "Exporting" : "Measuring")} {name}");
                    CalculationOptions options = new(Mode: mode);
                    CalculationSnapshot snapshot = CalculationSnapshot.Capture(cards, combos, 5, groups,
                        CalculationWorkPolicy.Interactive, storagePolicy: CalculationStoragePolicy.Standard, options: options);
                    if (exportOnly) {
                        output.Add(new {
                            Name = name, Mode = mode.ToString(), DeckSize = size, HandSize = 5, Shape = shape,
                            Storage = "Standard", Input = JsonSerializer.Deserialize<JsonElement>(snapshot.Json)
                        });
                        SaveDangerCheckpoint(outputPath, output);
                        continue;
                    }
                    List<double> milliseconds = [];
                    List<long> work = [];
                    List<long> entries = [];
                    List<long> cells = [];
                    ProbabilityCalculationResult? result = null;
                    ProbabilityCalculationLimitReason? limit = null;
                    for (int trial = 0; trial < trials; trial++) {
                        result = null;
                        limit = null;
                        WorkBudget budget = new(mode == CalculationModel.Exact
                            ? CalculationWorkPolicy.Unlimited : CalculationWorkPolicy.Interactive, CalculationStoragePolicy.Standard);
                        AppendDangerSample(outputPath, new { Name = name, Trial = trial + 1, Phase = "Started" });
                        long allocationStart = GC.GetAllocatedBytesForCurrentThread();
                        Stopwatch timer = Stopwatch.StartNew();
                        try {
                            result = new ProbabilityCalculatorService().CalculateWithBudget(cards, combos, 5, groups, budget, options);
                        }
                        catch (ProbabilityCalculationLimitException exception) {
                            limit = exception.Reason;
                        }
                        catch (Exception exception) {
                            AppendDangerSample(outputPath, new {
                                Name = name, Trial = trial + 1, Phase = "Failed", Exception = exception.GetType().Name,
                                Milliseconds = timer.Elapsed.TotalMilliseconds, Work = budget.Spent,
                                PeakEntries = budget.PeakEntries, PeakCells = budget.PeakCells
                            });
                            throw;
                        }
                        timer.Stop();
                        AppendDangerSample(outputPath, new {
                            Name = name, Trial = trial + 1, Phase = "Completed",
                            Outcome = limit?.ToString() ?? "Success", Milliseconds = timer.Elapsed.TotalMilliseconds,
                            Work = budget.Spent, PeakEntries = budget.PeakEntries, PeakCells = budget.PeakCells,
                            AllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocationStart, Result = result
                        });
                        TestContext.Progress.WriteLine($"{name} native sample {trial + 1}: {limit?.ToString() ?? "Success"}, {timer.Elapsed.TotalMilliseconds:F2} ms");
                        milliseconds.Add(timer.Elapsed.TotalMilliseconds);
                        work.Add(budget.Spent);
                        entries.Add(budget.PeakEntries);
                        cells.Add(budget.PeakCells);
                    }
                    AppendDangerSample(outputPath, new { Name = name, Phase = "WireStarted" });
                    string wireJson = mode == CalculationModel.Exact
                        ? CalculationWire.ExecuteInteractiveExact(snapshot.Json) : CalculationWire.Execute(snapshot.Json);
                    AppendDangerSample(outputPath, new {
                        Name = name, Phase = "WireCompleted", Response = JsonSerializer.Deserialize<JsonElement>(wireJson)
                    });
                    CalculationResponse wire = JsonSerializer.Deserialize<CalculationResponse>(wireJson)!;
                    Assert.That(wire.LimitReason, Is.EqualTo(limit));
                    Assert.That(JsonSerializer.Serialize(wire.Result), Is.EqualTo(JsonSerializer.Serialize(result)),
                        "Compare every result fraction, row and proof metadata, not percentages alone.");
                    Assert.That(work.Distinct().Count(), Is.EqualTo(1));
                    Assert.That(entries.Distinct().Count(), Is.EqualTo(1));
                    Assert.That(cells.Distinct().Count(), Is.EqualTo(1));
                    if (shape == "heavy" && mode == CalculationModel.Exact) {
                        Assert.That(limit, Is.Null, "The heavy Exact calculation must complete at Standard Storage.");
                        // Frozen from the original full physical frontier with diagnostic-only
                        // larger storage; small independent oracles also cover the streamed route.
                        string[] expected = size == 40 ? [
                            "19155694438132844204083/22830578417398752000000",
                            "111099644933409857/165035354964480000",
                            "159817660221030078031/304407712231983360000",
                            "34400725571562104809/203844450155346000000",
                            "660816141258725116781/913223136695950080000",
                            "34400725571562104809/203844450155346000000"
                        ] : [
                            "1452870058184223003498319/2218129686429392736000000",
                            "4935584606343002860291/12779347987659052800000",
                            "1130830528179953545339391/3105381561001149830400000",
                            "38681233721651543104931/188695060130278201500000",
                            "10584126355376122068727271/21737670927008048812800000",
                            "38681233721651543104931/188695060130278201500000"
                        ];
                        CalculationFractions fractions = wire.Result!.Contract!.Fractions!;
                        Assert.That(new[] { fractions.Total }.Concat(fractions.Combos).Concat(fractions.Groups)
                            .Select(value => $"{value.Numerator}/{value.Denominator}"), Is.EqualTo(expected));
                    }
                    if (limit is not null) {
                        Assert.That(wire.Contract!.Fractions, Is.Null);
                        Assert.That(wire.Contract.Accuracy, Is.Null);
                        Assert.That(wire.Contract.Completion, Is.EqualTo(limit == ProbabilityCalculationLimitReason.Storage
                            ? CalculationCompletion.StorageLimit : CalculationCompletion.WorkLimit));
                    }
                    output.Add(new {
                        Name = name, Mode = mode.ToString(), DeckSize = size, HandSize = 5, Shape = shape,
                        Storage = "Standard", Outcome = limit?.ToString() ?? "Success",
                        Milliseconds = milliseconds, Work = work, PeakEntries = entries, PeakCells = cells,
                        Input = JsonSerializer.Deserialize<JsonElement>(snapshot.Json),
                        Response = JsonSerializer.Deserialize<JsonElement>(wireJson)
                    });
                    SaveDangerCheckpoint(outputPath, output);
                    TestContext.Progress.WriteLine($"{name}: {limit?.ToString() ?? "Success"}; "
                        + $"ms={string.Join(',', milliseconds)}; Work={string.Join(',', work)}; "
                        + $"entries={string.Join(',', entries)}; cells={string.Join(',', cells)}");
                }
            }
        }
        SaveDangerCheckpoint(outputPath, output);

        Card Preset(int id, int copies) {
            MaintainedCardEffect effect = CardEffectRegistry.Default.Find(id)!;
            List<CategoryBase> properties = effect.Plan.Operation == DrawOperation.DangerReveal ? [dark] : [];
            return new Card(properties, copies, effect.Name, id: id.ToString(), externalCardId: id).WithEffect(effect.Plan, effect.Profile);
        }
    }

    private static void SaveDangerCheckpoint(string path, List<object> output) {
        string pendingPath = path + ".pending";
        File.WriteAllText(pendingPath, JsonSerializer.Serialize(output));
        File.Move(pendingPath, path, true);
    }

    private static void AppendDangerSample(string path, object sample) {
        using FileStream stream = new(path + ".samples.jsonl", FileMode.Append, FileAccess.Write, FileShare.Read);
        using StreamWriter writer = new(stream, leaveOpen: true);
        writer.WriteLine(JsonSerializer.Serialize(sample));
        writer.Flush();
        stream.Flush(flushToDisk: true);
    }

    private static Card Effect(int id, int copies) => CuratedDrawEffectPresets.ApplyToSelectedCardIfPristine(
        new([], copies, id: id.ToString(), externalCardId: id), new() { Id = id });
}
