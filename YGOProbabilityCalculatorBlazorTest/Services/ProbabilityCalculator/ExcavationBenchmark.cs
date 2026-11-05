using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture, Explicit("Excavation production acceptance measurements; no timing correctness gate"), NonParallelizable]
public sealed class ExcavationBenchmark {
    [Test]
    public void Measure() {
        using MeasurementCulture culture = new();
        string? requested = Environment.GetEnvironmentVariable("YGO_EXCAVATION_TRIALS");
        if (!int.TryParse(requested ?? "3", NumberStyles.None, CultureInfo.InvariantCulture, out int trials) || trials is < 1 or > 20) {
            throw new ArgumentException("YGO_EXCAVATION_TRIALS must be an integer from 1 to 20.");
        }
        string? filter = Environment.GetEnvironmentVariable("YGO_EXCAVATION_CASE");
        string? exclude = Environment.GetEnvironmentVariable("YGO_EXCAVATION_EXCLUDE");
        string? modeFilter = Environment.GetEnvironmentVariable("YGO_EXCAVATION_MODE");
        if (modeFilter is not null && !new[] { "Fast", "Exact" }.Contains(modeFilter, StringComparer.OrdinalIgnoreCase)) {
            throw new ArgumentException("YGO_EXCAVATION_MODE must be Fast or Exact.");
        }
        string output = FrozenCalculationFixture.OutputPath("YGO_EXCAVATION_BENCHMARK");
        List<FrozenCalculationFixture> exports = [];
        foreach ((string name, SessionState session) in Fixtures()) {
            if (filter is not null && !name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || exclude is not null && name.Contains(exclude, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            foreach (CalculationModel mode in new[] { CalculationModel.Fast, CalculationModel.Exact }) {
                if (modeFilter is not null && !mode.ToString().Equals(modeFilter, StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }
                CalculationSnapshot snapshot = CalculationSnapshot.Capture(session.Cards, session.Combos, session.HandSize,
                    session.ComboGroups, CalculationWorkPolicy.Interactive, storagePolicy: CalculationStoragePolicy.Standard, options: new(Mode: mode));
                string? execution = mode == CalculationModel.Exact ? "interactive-exact-v1" : null;
                List<double> times = [];
                List<long> allocations = [];
                JsonElement reference = default;
                for (int trial = 0; trial <= trials; trial++) {
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    long start = Stopwatch.GetTimestamp();
                    string reply = FrozenCalculationFixture.Execute(snapshot.Json, execution);
                    times.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                    allocations.Add(GC.GetAllocatedBytesForCurrentThread() - before);
                    JsonElement response = JsonSerializer.Deserialize<JsonElement>(reply);
                    if (trial == 0) {
                        reference = response;
                    } else {
                        Assert.That(JsonElement.DeepEquals(response, reference), Is.True, name + "/" + mode);
                    }
                }
                TestContext.Out.WriteLine($"{name}/{mode}: first={times[0]:F2}ms; warmMedian={times.Skip(1).Order().ElementAt(trials / 2):F2}ms; "
                    + $"allocatedMedian={allocations.Skip(1).Order().ElementAt(trials / 2)}");
                exports.Add(new(name + "/" + mode, snapshot.Json, execution, reference));
                FrozenCalculationFixture.Validate(exports);
                File.WriteAllText(output, JsonSerializer.Serialize(exports));
            }
        }
        FrozenCalculationFixture.Validate(exports);
    }

    internal static IEnumerable<(string Name, SessionState Session)> Fixtures() {
        yield return ("40-baseline", Create(40, 3, disabled: true));
        yield return ("40-duality", Create(40, 3));
        yield return ("40-prosperity", Create(40, 6));
        yield return ("60-prosperity", Create(60, 6));
        yield return ("40-both-pots", Create(40, 6, secondPot: true));
        yield return ("40-duality-search", Create(40, 3, search: true));
        yield return ("40-prosperity-search", Create(40, 6, search: true));
        yield return ("40-duality-draw-search", Create(40, 3, search: true, draw: true));
        yield return ("40-both-draw-search", Create(40, 6, secondPot: true, search: true, draw: true));
        yield return ("40-duality-hidden-search", Create(40, 3, search: true, draw: true, hidden: true));
        yield return ("60-prosperity-overlap-stress", Create(60, 6, secondPot: true, search: true, stress: true));
    }

    private static SessionState Create(
        int size,
        int amount,
        bool disabled = false,
        bool secondPot = false,
        bool search = false,
        bool draw = false,
        bool hidden = false,
        bool stress = false
    ) {
        CategoryBase a = new("Starter");
        CategoryBase b = new("Extender");
        CategoryBase c = new("Shared role");
        Card pot = CardEffectRegistry.Default.RestoreBuiltIn(new([], amount == 6 ? 1 : 3,
            amount == 6 ? "Pot of Prosperity" : "Pot of Duality", id: "pot", externalCardId: amount == 6 ? 84211599 : 98645731));
        List<Card> cards = [pot.WithSimulationEnabled(!disabled),
            new([a, c], 3, "Starter A", id: "starter-a"), new([a], 3, "Starter B", id: "starter-b"),
            new([b, c], 3, "Extender A", id: "extender-a"), new([a, b], 3, "Flexible", id: "flexible")];
        if (secondPot) {
            cards.Add(CardEffectRegistry.Default.RestoreBuiltIn(new([], 3, "Pot of Duality", id: "duality", externalCardId: 98645731)));
        }
        if (search) {
            cards.Add(new([], 3, "Starter search", id: "search-a", search: new(CardSelector.For(a), false), oncePerTurn: true));
            cards.Add(new([], 1, "Flexible search", id: "search-any", search: new(CardSelector.AnyOf([a.Identity, b.Identity]), false), oncePerTurn: true));
        }
        if (draw) {
            cards.Add(new([], 1, hidden ? "Hidden-loss draw" : "Draw two", id: "draw", drawCount: 2,
                preDrawDeckRemovalCount: hidden ? 10 : 0));
        }
        List<Combo> combos = [new([new(a, 1, 5), new(b, 1, 5)], "Starter + extender", groupId: "engine"),
            new([new(a, 2, 5)], "Two starters", groupId: "engine"),
            new([new(c, 2, 5)], "Two shared roles", groupId: "engine"),
            new([], "Named pair", groupId: "engine", cards: [new("starter-a", 1, 5), new("extender-a", 1, 5)]),
            new([new(a, 1, 2), new(b, 1, 2), new(c, 0, 1)], "Bounded overlap", groupId: "bounded"),
            new([new(a, 1, 5), new(c, 0, 0)], "Starter without shared role", groupId: "bounded"),
            new([ComboCategory.SameCard([a, b], 1, 2)], "Flexible identity", groupId: "engine")];
        if (stress) {
            for (int index = 0; index < 8; index++) {
                string id = $"additional-{index}";
                cards.Add(new(index % 2 == 0 ? [a, c] : [b, c], 3, $"Additional role {index}", id: id));
                combos.Add(new([new(index % 2 == 0 ? b : a, 1, 5)], $"Named overlap {index}", groupId: "engine", cards: [new(id, 1, 5)]));
            }
        }
        cards.Add(new([], size - cards.Sum(card => card.Copies), "Other cards", id: "filler"));

        return new() { Cards = cards, Categories = [a, b, c], Combos = combos,
            ComboGroups = [new("engine", "Engine"), new("bounded", "Bounded")], HandSize = 5 };
    }
}
