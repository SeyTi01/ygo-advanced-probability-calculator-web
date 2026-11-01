using System.Diagnostics;
using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;
using YGOProbabilityCalculatorBlazor.Services.Session;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture, Explicit("Excavation production acceptance measurements; no timing correctness gate"), NonParallelizable]
public sealed class ExcavationBenchmark {
    [Test]
    public void Measure() {
        int trials = int.TryParse(Environment.GetEnvironmentVariable("YGO_EXCAVATION_TRIALS"), out int requested)
            ? Math.Clamp(requested, 1, 20) : 3;
        string? filter = Environment.GetEnvironmentVariable("YGO_EXCAVATION_CASE");
        string? exclude = Environment.GetEnvironmentVariable("YGO_EXCAVATION_EXCLUDE");
        string? modeFilter = Environment.GetEnvironmentVariable("YGO_EXCAVATION_MODE");
        string? output = Environment.GetEnvironmentVariable("YGO_EXCAVATION_BENCHMARK");
        List<object> exports = [];
        SessionService codec = new(Moq.Mock.Of<Microsoft.JSInterop.IJSRuntime>(),
            new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());
        foreach ((string name, SessionState session) in Fixtures()) {
            if (filter is not null && !name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || exclude is not null && name.Contains(exclude, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            foreach (CalculationModel mode in new[] { CalculationModel.Fast, CalculationModel.Exact }) {
                if (modeFilter is not null && !mode.ToString().Equals(modeFilter, StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }
                CalculationOptions options = new(Mode: mode);
                CalculationSnapshot snapshot = CalculationSnapshot.Capture(session.Cards, session.Combos, session.HandSize,
                    session.ComboGroups, CalculationWorkPolicy.Interactive, storagePolicy: CalculationStoragePolicy.Standard, options: options);
                List<double> times = [];
                List<long> allocations = [];
                WorkBudget budget = null!;
                CalculationResponse response = null!;
                string outcome = "Success";
                for (int trial = 0; trial < trials + 1; trial++) {
                    budget = new(mode == CalculationModel.Exact ? CalculationWorkPolicy.Unlimited : CalculationWorkPolicy.Interactive,
                        CalculationStoragePolicy.Standard);
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    Stopwatch timer = Stopwatch.StartNew();
                    try {
                        ProbabilityCalculationResult result = new ProbabilityCalculatorService().CalculateWithBudget(session.Cards,
                            session.Combos, session.HandSize, session.ComboGroups, budget, options);
                        response = new(result, null);
                    }
                    catch (ProbabilityCalculationLimitException exception) {
                        outcome = exception.Reason.ToString();
                        response = new(null, exception.Message, exception.Reason,
                            Contract: CalculationResultContract.Failure(mode,
                                exception.Reason == ProbabilityCalculationLimitReason.Work ? CalculationCompletion.WorkLimit : CalculationCompletion.StorageLimit,
                                exception.Message));
                    }
                    timer.Stop();
                    // Preserve cold and warm trials separately in the export.
                    times.Add(timer.Elapsed.TotalMilliseconds);
                    allocations.Add(GC.GetAllocatedBytesForCurrentThread() - before);
                }
                ProbabilityCalculationResult? measured = response.Result;
                CalculationFractions? fractions = measured?.Contract?.Fractions;
                string[]? exact = fractions is null ? null : new[] { fractions.Total }.Concat(fractions.Combos).Concat(fractions.Groups)
                    .Select(value => $"{value.Numerator}/{value.Denominator}").ToArray();
                TestContext.Out.WriteLine($"{name}/{mode}: {outcome}; cold={times[0]:F2}ms; warmMedian={times.Skip(1).Order().ElementAt(trials / 2):F2}ms; "
                    + $"work={budget.Spent}; cells={budget.PeakCells}; allocatedMedian={allocations.Skip(1).Order().ElementAt(trials / 2)}; p={measured?.TotalProbability:R}");
                exports.Add(new {
                    Name = name, Mode = mode.ToString(), Outcome = outcome, Milliseconds = times, AllocatedBytes = allocations,
                    Probability = measured?.TotalProbability, Work = budget.Spent, Entries = budget.PeakEntries, Cells = budget.PeakCells,
                    ReferenceResults = measured, ReferenceResponse = response, ExactResults = exact,
                    Session = JsonSerializer.Deserialize<JsonElement>(codec.SerializeSession(session)),
                    Input = JsonSerializer.Deserialize<JsonElement>(snapshot.Json)
                });
                if (output is not null) {
                    File.WriteAllText(output, JsonSerializer.Serialize(exports));
                }
            }
        }
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
