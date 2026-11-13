using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

// Opt-in issue #283 corpus. Export baseline references once, then use
// PerformanceAuditBenchmark.Replay and measure-worker.cjs unchanged.
// YGO_AUDIT_OUTPUT selects a new output file; YGO_AUDIT_CASE filters full names.
// Names are <workload>/<StorageTier>/<Fast|Exact>, allowing exact pairing.
[TestFixture, Explicit("Storage-tier research corpus; no routine CI or timing assertions"), NonParallelizable]
public sealed class StorageTierBenchmark {
    [Test]
    public void ExportRequests() {
        using MeasurementCulture culture = new();
        string output = NewOutput();
        StorageRequest[] requests = SelectedRequests();
        File.WriteAllText(output, JsonSerializer.Serialize(requests));
        TestContext.Out.WriteLine($"Exported {requests.Length} requests without executing calculations.");
    }

    [Test]
    public void Export() {
        using MeasurementCulture culture = new();
        string output = NewOutput();
        StorageRequest[] requests = SelectedRequests();
        List<FrozenCalculationFixture> fixtures = [];
        foreach (StorageRequest request in requests) {
            // Flush before entering Exact so an external research cutoff can
            // identify the censored request. This is not a production deadline.
            TestContext.Progress.WriteLine("Starting " + request.Name);
            string reply = FrozenCalculationFixture.Execute(request.Json, request.Execution);
            JsonElement response = JsonSerializer.Deserialize<JsonElement>(reply);
            fixtures.Add(new(request.Name, request.Json, request.Execution, response));
            FrozenCalculationFixture.Validate(fixtures);
            // Typed Work/Storage failures are evidence too. An interrupted run
            // never leaves a partial corpus under the completed output name.
            File.WriteAllText(output + ".partial.json", JsonSerializer.Serialize(fixtures));
            TestContext.Progress.WriteLine("Captured " + request.Name);
        }
        File.WriteAllText(output, JsonSerializer.Serialize(fixtures));
    }

    internal static IEnumerable<StorageRequest> Requests() {
        foreach ((string name, SessionState session) in Workloads()) {
            foreach (CalculationStorageTier tier in Enum.GetValues<CalculationStorageTier>()) {
                foreach (CalculationModel mode in new[] { CalculationModel.Fast, CalculationModel.Exact }) {
                    CalculationSnapshot snapshot = CalculationSnapshot.Capture(
                        session.Cards,
                        session.Combos,
                        session.HandSize,
                        session.ComboGroups,
                        CalculationWorkPolicy.Interactive,
                        storagePolicy: CalculationStoragePolicy.ForTier(tier),
                        options: new(session.DrawActivationPriority, Mode: mode)
                    );
                    // The interactive Exact entry point ignores the bounded
                    // wire allowance and applies the UI's unlimited Work policy.
                    yield return new(name + "/" + tier + "/" + mode, snapshot.Json,
                        mode == CalculationModel.Exact ? "interactive-exact-v1" : null);
                }
            }
        }
    }

    internal static IEnumerable<(string Name, SessionState Session)> Workloads() {
        foreach (int size in new[] { 40, 50, 60 }) {
            yield return ($"ordinary-{size}", Ordinary(size, false, false, false));
            yield return ($"search-{size}", Ordinary(size, true, false, false));
            yield return ($"draw-{size}", Ordinary(size, false, true, false));
            yield return ($"mixed-hidden-{size}", Ordinary(size, true, true, true));
        }
        yield return ("ordered-pressure10-40", Pressure(10, false));
        yield return ("ordered-pressure11-40", Pressure(11, false));
        yield return ("ordered-priority11-40", Pressure(11, true));
        // Use the original saved application plans behind the neutral pressure
        // snapshots. Those snapshots only rename IDs in ordinal order; rebuilding
        // their resolved memberships as new categories could alter keep ordering.
        yield return ("hidden-deck-cost-40", SearchAvailabilityCertificateTest.Example("banish10").GetAwaiter().GetResult());
        yield return ("hidden-deck-mill-40", SearchAvailabilityCertificateTest.Example("mill2").GetAwaiter().GetResult());
        yield return ("search-pressure-opt-60", SearchPressure(60, 16));
        yield return ("search-pressure-opt-50", SearchPressure(50, 14));
    }

    private static SessionState Ordinary(int size, bool search, bool draw, bool hidden) {
        CategoryBase starter = new("Starter");
        CategoryBase extender = new("Extender");
        CategoryBase shared = new("Shared");
        List<Card> cards = [
            new([starter, shared], 3, "Starter A", id: "starter-a"),
            new([starter], 3, "Starter B", id: "starter-b"),
            new([extender, shared], 3, "Extender A", id: "extender-a"),
            new([starter, extender], 3, "Flexible", id: "flexible")
        ];
        if (search) {
            cards.Add(new([], 3, "Starter search", id: "search-a", search: new(CardSelector.For(starter), false), oncePerTurn: true));
            cards.Add(new([], 2, "Flexible search", id: "search-any",
                search: new(CardSelector.AnyOf([starter.Identity, extender.Identity]), false), oncePerTurn: true));
        }
        if (draw) {
            cards.Add(new([], 2, "Draw two", id: "draw-two", drawCount: 2,
                preDrawDeckRemovalCount: hidden ? 10 : 0, postDrawDiscardCount: hidden ? 1 : 0));
            cards.Add(new([], 1, "Draw one", id: "draw-one", drawCount: 1, drawOncePerTurn: false));
        }
        cards.Add(new([], size - cards.Sum(card => card.Copies), "Other cards", id: "filler"));
        List<Combo> combos = [
            new([new(starter, 1, 5), new(extender, 1, 5)], "Starter plus extender", groupId: "engine"),
            new([new(starter, 2, 5)], "Two starters", groupId: "engine"),
            new([new(shared, 2, 5)], "Two shared roles", groupId: "engine"),
            new([], "Named pair", groupId: "engine", cards: [new("starter-a", 1, 5), new("extender-a", 1, 5)]),
            new([new(starter, 1, 2), new(extender, 1, 2), new(shared, 0, 1)], "Bounded overlap", groupId: "bounded"),
            new([new(starter, 1, 5), new(shared, 0, 0)], "Starter without shared", groupId: "bounded"),
            new([ComboCategory.SameCard([starter, extender], 1, 2)], "Flexible identity", groupId: "engine")
        ];

        return new() {
            Cards = cards, Categories = [starter, extender, shared], Combos = combos,
            ComboGroups = [new("engine", "Engine"), new("bounded", "Bounded"), new("empty", "Empty")], HandSize = 5
        };
    }

    private static SessionState Pressure(int roles, bool customPriority) {
        List<CategoryBase> categories = Enumerable.Range(0, roles).Select(index => new CategoryBase("Role " + index)).ToList();
        List<Card> cards = categories.Select((category, index) => new Card([category], 3, "Role " + index, id: "r" + index)).ToList();
        cards.AddRange([
            new([], 2, "Draw two discard one", id: "d", drawCount: 2, postDrawDiscardCount: 1),
            new([], 2, "Draw one discard one", id: "e", drawCount: 1, drawOncePerTurn: false, postDrawDiscardCount: 1),
            new([], 1, "Role zero search", id: "s", search: new(CardSelector.For(categories[0]), false))
        ]);
        cards.Add(new([], 40 - cards.Sum(card => card.Copies), "Other cards", id: "f"));
        List<Combo> combos = categories.Select((category, index) => new Combo([new(category, 1, 5)],
            "Role " + index, groupId: index % 2 == 0 ? "even" : "odd")).ToList();

        return new() {
            Cards = cards, Categories = categories, Combos = combos, HandSize = 5,
            ComboGroups = [new("even", "Even roles"), new("odd", "Odd roles")],
            DrawActivationPriority = customPriority ? new(DrawActivationMode.CustomPriority, ["e", "d", "s"]) : DrawActivationPriority.Default
        };
    }

    private static SessionState SearchPressure(int size, int rowCount) {
        // The 60-card instance matches neutral StorageTierProfile.SearchPressure.
        // The 50-card neighbor removes two ordinary rows and adjusts filler;
        // masks, source allowances, goals, groups and fixed IDs stay unchanged.
        List<CategoryBase> categories = Enumerable.Range(0, 8).Select(index => new CategoryBase("role" + index)).ToList();
        List<Card> cards = Enumerable.Range(0, rowCount).Select(row => new Card(
            categories.Where((_, index) => (row + index) % 4 < 2), 3, "Role " + row, id: "r" + row)).ToList();
        cards.AddRange(Enumerable.Range(0, 3).Select(index => new Card([], 2, "Search " + index,
            id: "s" + index, search: new(CardSelector.For(categories[index]), false), oncePerTurn: true)));
        cards.Add(new([], size - cards.Sum(card => card.Copies), "Other cards", id: "f"));
        List<Combo> combos = Enumerable.Range(0, 16).Select(index => new Combo(
            [new(categories[index % 8], 1, 3), new(categories[(index + 3) % 8], 1, 3)],
            "Goal " + index, groupId: index % 2 == 0 ? "even" : "odd")).ToList();

        return new() {
            Cards = cards, Categories = categories, Combos = combos, HandSize = 5,
            ComboGroups = [new("even", "Even roles"), new("odd", "Odd roles")]
        };
    }

    private static StorageRequest[] SelectedRequests() {
        string? filter = Environment.GetEnvironmentVariable("YGO_AUDIT_CASE");
        StorageRequest[] requests = Requests().Where(request => filter is null
            || request.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (requests.Length == 0) {
            throw new ArgumentException("YGO_AUDIT_CASE matched no storage fixtures.");
        }
        return requests;
    }

    private static string NewOutput() {
        string output = FrozenCalculationFixture.OutputPath("YGO_AUDIT_OUTPUT");
        if (File.Exists(output) || File.Exists(output + ".partial.json")) {
            throw new ArgumentException("Choose a new output path; frozen research captures must not be overwritten.");
        }
        return output;
    }

    internal sealed record StorageRequest(string Name, string Json, string? Execution);
}
