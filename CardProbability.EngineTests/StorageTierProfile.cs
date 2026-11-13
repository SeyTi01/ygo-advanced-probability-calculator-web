using System.Globalization;
using System.Text.Json;
using CardProbability.EngineTests.Fixtures;
using CardProbability.Internal;
using CardProbability.Internal.DrawResolution;
using CardProbability.Internal.Resources;
using CardProbability.Internal.Search;
using Core = CardProbability.Internal.Contracts;
using Model = CardProbability.Internal.Model;

namespace CardProbability.EngineTests;

// Explicit diagnostic execution, separate from uninstrumented timing runs.
// CARD_STORAGE_PROFILE_CASE: optional name substring; TIERS and MODES: comma-separated enums.
// CARD_STORAGE_PROFILE_OUTPUT: JSON path; CUTOFF_MS: external harness cancellation (default 60000).
// CARD_STORAGE_PROFILE_WORK: optional positive allowance or "unlimited"; otherwise Fast=150M, Exact=unlimited.
[TestFixture, Explicit("Storage cache diagnostics; not a timing benchmark"), NonParallelizable]
internal sealed class StorageTierProfile {
    [Test]
    public void Profile() => Run(false);

    // Uninstrumented public execution captures comparable terminal values and metrics.
    [Test]
    public void ProfilePublic() => Run(false, true);

    // This forced ordered projection exposes frontier accounting, not production Fast/Exact routing.
    [Test]
    public void ProfileOrderedProjection() => Run(true);

    private static void Run(bool orderedProjection, bool publicOnly = false) {
        string? filter = Environment.GetEnvironmentVariable("CARD_STORAGE_PROFILE_CASE");
        string? output = Environment.GetEnvironmentVariable("CARD_STORAGE_PROFILE_OUTPUT");
        Core.CalculationStorageTier[] tiers = SelectEnums("CARD_STORAGE_PROFILE_TIERS", Enum.GetValues<Core.CalculationStorageTier>());
        CalculationMode[] modes = orderedProjection ? [CalculationMode.OptimisticSearchProjection]
            : SelectEnums("CARD_STORAGE_PROFILE_MODES", new[] { CalculationMode.Fast, CalculationMode.Exact });
        if (modes.Any(mode => mode is not (CalculationMode.Fast or CalculationMode.Exact or CalculationMode.OptimisticSearchProjection))) {
            throw new ArgumentException("Profile modes must be Fast, Exact or OptimisticSearchProjection.");
        }
        int cutoff = int.Parse(Environment.GetEnvironmentVariable("CARD_STORAGE_PROFILE_CUTOFF_MS") ?? "60000", CultureInfo.InvariantCulture);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cutoff);
        EngineCase[] fixtures = Cases().Where(fixture => filter is null || fixture.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray();
        Assert.That(fixtures, Is.Not.Empty, "CARD_STORAGE_PROFILE_CASE matched no fixtures.");
        List<object> measurements = [];
        foreach (EngineCase fixture in fixtures) {
            foreach (CalculationMode mode in modes) {
                foreach (Core.CalculationStorageTier tier in tiers) {
                    Core.CalculationStoragePolicy storage = Core.CalculationStoragePolicy.ForTier(tier);
                    Core.CalculationWorkPolicy work = Work(mode);
                    CalculationOptions options = new(mode, new(work.IsUnlimited ? null : work.WorkUnits, storage.EntryLimit, storage.CellLimit),
                        fixture.Options.ActivationOrder, fixture.Options.PriorityEntryIds);
                    using CancellationTokenSource cancellation = new(TimeSpan.FromMilliseconds(cutoff));
                    SearchCountingDiagnostics diagnostics = new();
                    // Match the public boundary: strategy inputs are numeric limits, not a UI tier enum.
                    WorkBudget budget = new(work, new Core.CalculationStoragePolicy(storage.EntryLimit, storage.CellLimit), cancellation.Token) { SearchDiagnostics = diagnostics };
                    PostDrawCalculation? ordered = null;
                    CalculationResult? publicResult = null;
                    Core.ProbabilityCalculationResult? result = null;
                    Core.CalculationFractions? orderedFractions = null;
                    string outcome = "Completed";
                    string? detail = null;
                    Save(new { fixture.Name, Mode = mode.ToString(), Tier = tier.ToString(), Outcome = "Running" });
                    long allocated = GC.GetAllocatedBytesForCurrentThread();
                    try {
                        if (publicOnly) {
                            publicResult = CalculationEngine.Calculate(fixture.Request, options, cancellation.Token);
                            outcome = publicResult.Status == CalculationStatus.Cancelled && cancellation.IsCancellationRequested
                                ? "HarnessCutoff" : publicResult.Status.ToString();
                        }
                        else {
                            PreparedCalculation prepared = PreparedCalculation.Create(fixture.Request, options, budget);
                            Model.DrawActivationPriority priority = new(
                                options.ActivationOrder == ActivationOrder.Priority ? Model.DrawActivationMode.CustomPriority : Model.DrawActivationMode.EncounterOrder,
                                options.PriorityEntryIds);
                            if (orderedProjection) {
                                ordered = new(prepared.Cards, fixture.Request.HandSize, budget, priority);
                                ExactCalculationResult exact = ordered.CalculateSearch(prepared.Combos, prepared.Groups, false, new(prepared.Cards, budget));
                                orderedFractions = new(exact.Total.ToFraction(), [.. exact.Combos.Select(row => row.ToFraction())],
                                    [.. exact.Groups.Select(row => row.ToFraction())]);
                            }
                            else {
                                Model.CalculationModel runtimeMode = mode == CalculationMode.Exact ? Model.CalculationModel.Exact : Model.CalculationModel.Fast;
                                Core.CalculationOptions execution = new(priority, mode == CalculationMode.OptimisticSearchProjection, runtimeMode);
                                result = new CalculationRuntime().CalculateWithBudget(prepared.Cards, prepared.Combos, fixture.Request.HandSize,
                                    prepared.Groups, budget, execution);
                            }
                        }
                    }
                    catch (Core.ProbabilityCalculationLimitException error) {
                        outcome = error.Reason + "Limit";
                        detail = error.Message;
                    }
                    catch (OperationCanceledException) when (cancellation.IsCancellationRequested) {
                        outcome = "HarnessCutoff";
                    }
                    catch (Core.UnsupportedExactCalculationException error) {
                        outcome = "Unsupported";
                        detail = error.Message;
                    }
                    long diagnosticAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
                    measurements.Add(new {
                        fixture.Name, Mode = mode.ToString(), Tier = tier.ToString(), Outcome = outcome, Detail = detail,
                        Copies = fixture.Request.Entries.Sum(entry => entry.Copies), Goals = fixture.Request.Goals.Length,
                        options.Policy, Work = publicResult?.Metrics.WorkSpent ?? budget.Spent,
                        PeakEntries = publicResult?.Metrics.PeakEntries ?? budget.PeakEntries,
                        PeakCells = publicResult?.Metrics.PeakCells ?? budget.PeakCells,
                        PackedLayers = ordered?.PackedLayers, PeakConcurrentCells = ordered?.PeakConcurrentCells,
                        LargestStructure = ordered?.LargestStructure, DiagnosticAllocatedBytes = diagnosticAllocatedBytes,
                        PublicResult = publicResult, Contract = result?.Contract, Certificate = result?.SearchAvailability, OrderedFractions = orderedFractions,
                        diagnostics.Counts, DiagnosticPhaseMilliseconds = diagnostics.Milliseconds
                    });
                    Save(null);
                    TestContext.Out.WriteLine($"{fixture.Name}/{mode}/{tier}: {outcome}; Work={publicResult?.Metrics.WorkSpent ?? budget.Spent}; Entries={publicResult?.Metrics.PeakEntries ?? budget.PeakEntries}; Cells={publicResult?.Metrics.PeakCells ?? budget.PeakCells}");
                }
            }
        }

        void Save(object? running) {
            string json = JsonSerializer.Serialize(new {
                SchemaVersion = 1, Path = orderedProjection ? "ForcedOrderedProjection" : publicOnly ? "UninstrumentedPublic" : "ProductionRouting",
                HarnessCutoffMilliseconds = cutoff,
                Notes = "Instrumented diagnostics are not benchmark timings. Allocations include diagnostic maps. Per-structure peaks are not total memory. Null frontier fields mean unavailable on this path.",
                Measurements = measurements, Running = running
            }, new JsonSerializerOptions { WriteIndented = true });
            if (output is null) {
                if (running is null) {
                    TestContext.Out.WriteLine(json);
                }
            }
            else {
                string absolute = Path.GetFullPath(output);
                Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
                File.WriteAllText(absolute, json);
            }
        }
    }

    private static Core.CalculationWorkPolicy Work(CalculationMode mode) {
        string? requested = Environment.GetEnvironmentVariable("CARD_STORAGE_PROFILE_WORK");
        return requested is null ? mode == CalculationMode.Exact ? Core.CalculationWorkPolicy.Unlimited : Core.CalculationWorkPolicy.Interactive
            : requested.Equals("unlimited", StringComparison.OrdinalIgnoreCase) ? Core.CalculationWorkPolicy.Unlimited
            : new(long.Parse(requested, CultureInfo.InvariantCulture));
    }

    private static T[] SelectEnums<T>(string variable, T[] defaults) where T : struct, Enum {
        string? value = Environment.GetEnvironmentVariable(variable);
        return value is null ? defaults : value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(name => Enum.TryParse(name, true, out T parsed) && Enum.IsDefined(parsed)
                ? parsed : throw new ArgumentException($"Invalid {variable}: {name}")).ToArray();
    }

    private static IEnumerable<EngineCase> Cases() {
        foreach (string name in new[] { "hidden-deck-cost", "hidden-deck-mill", "mixed-cost-pressure" }) {
            yield return NeutralPressureFixtures.Load(name);
        }
        yield return Pressure(10, 40, false);
        yield return Pressure(11, 40, false);
        yield return Pressure(11, 60, true);
        yield return SearchPressure();
    }

    private static EngineCase Pressure(int roles, int size, bool priority) {
        List<DeckEntry> entries = Enumerable.Range(0, roles).Select(index => new DeckEntry("r" + index, 3)).ToList();
        entries.AddRange([new("d", 2, new(DrawCount: 2, ActivationLimit: 1, RemovalCount: 1)),
            new("e", 2, new(DrawCount: 1, ActivationLimit: null, RemovalCount: 1)),
            new("s", 1, new(Search: new(new("role0"))))]);
        entries.Add(new("f", size - entries.Sum(entry => entry.Copies)));
        Goal[] goals = Enumerable.Range(0, roles).Select(index => new Goal("goal" + index,
            [Requirement.InMembership("role" + index, 1, 5)], groupId: "g")).ToArray();
        Membership[] memberships = Enumerable.Range(0, roles).Select(index => new Membership("role" + index, ["r" + index])).ToArray();
        return new($"pressure-{roles}-{size}" + (priority ? "-priority" : ""), new(entries, 5, goals, memberships, ["g"]),
            priority ? new(activationOrder: ActivationOrder.Priority, priorityEntryIds: ["e", "d"]) : new());
    }

    private static EngineCase SearchPressure() {
        List<DeckEntry> entries = Enumerable.Range(0, 16).Select(index => new DeckEntry("r" + index, 3)).ToList();
        Membership[] memberships = Enumerable.Range(0, 8).Select(index => new Membership("role" + index,
            Enumerable.Range(0, 16).Where(row => (row + index) % 4 < 2).Select(row => "r" + row))).ToArray();
        entries.AddRange(Enumerable.Range(0, 3).Select(index => new DeckEntry("s" + index, 2, new(Search: new(new("role" + index))))));
        entries.Add(new("f", 6));
        Goal[] goals = Enumerable.Range(0, 16).Select(index => new Goal("goal" + index,
            [Requirement.InMembership("role" + index % 8, 1, 3), Requirement.InMembership("role" + (index + 3) % 8, 1, 3)],
            groupId: index % 2 == 0 ? "even" : "odd")).ToArray();
        return new("search-pressure-60", new(entries, 5, goals, memberships, ["even", "odd"]), new());
    }
}
