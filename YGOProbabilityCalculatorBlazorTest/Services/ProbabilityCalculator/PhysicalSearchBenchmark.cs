using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Session;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture, Explicit("Physical search profiling; not a timing assertion"), NonParallelizable]
public sealed class PhysicalSearchBenchmark {
    [Test]
    public async Task Export() {
        List<(string Name, SessionState State, CalculationModel Mode, bool Unchanged)> cases = [];
        SessionState draw = await SearchAvailabilityCertificateTest.Example("one-draw2");
        SessionState count = await SearchAvailabilityCertificateTest.Example("one-draw2");
        for (int row = 0; row < count.Cards.Count; row++) {
            count.Cards[row] = count.Cards[row].WithSearch(null);
        }
        cases.Add(("opening", draw, CalculationModel.OpeningHand, true));
        cases.Add(("count-state", count, CalculationModel.Fast, true));
        cases.Add(("search-only", await SearchAvailabilityCertificateTest.Example("plain"), CalculationModel.Fast, true));
        cases.Add(("zero-cost", draw, CalculationModel.Exact, true));
        cases.Add(("fast-bound", await SearchAvailabilityCertificateTest.Example("mill2"), CalculationModel.Fast, true));
        cases.Add(("exact-mill2", await SearchAvailabilityCertificateTest.Example("mill2"), CalculationModel.Exact, false));
        cases.Add(("fast-banish10", await SearchAvailabilityCertificateTest.Example("banish10"), CalculationModel.Fast, true));
        cases.Add(("exact-banish10", await SearchAvailabilityCertificateTest.Example("banish10"), CalculationModel.Exact, false));
        SessionState noSearchLoss = await SearchAvailabilityCertificateTest.Example("mill2");
        for (int row = 0; row < noSearchLoss.Cards.Count; row++) {
            noSearchLoss.Cards[row] = noSearchLoss.Cards[row].WithSearch(null);
        }
        cases.Add(("no-search-mill2", noSearchLoss, CalculationModel.Exact, true));
        SessionState Mini(string kind) => new() { Cards = ThreeCalculationModesTest.Counterexample(kind), HandSize = kind == "consumed" ? 1 : 2,
            Categories = [ThreeCalculationModesTest.Target], Combos = ThreeCalculationModesTest.Combos, ComboGroups = ThreeCalculationModesTest.Groups };
        cases.Add(("consumed", Mini("consumed"), CalculationModel.Fast, true));
        cases.Add(("physical-consumed", Mini("consumed"), CalculationModel.Exact, false));
        cases.Add(("discard", Mini("discard"), CalculationModel.Fast, true));
        cases.Add(("exact-unsupported", Mini("discard"), CalculationModel.Exact, false));
        List<object> output = [];
        SessionService codec = new(null!, new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());
        foreach ((string name, SessionState state, CalculationModel mode, bool unchanged) in cases) {
            JsonObject input = JsonNode.Parse(CalculationSnapshot.Capture(state.Cards, state.Combos, state.HandSize, state.ComboGroups,
                CalculationWorkPolicy.Default, options: new(Mode: mode)).Json)!.AsObject();
            JsonObject session = JsonNode.Parse(codec.SerializeSession(state))!.AsObject();
            session["CalculationModel"] = mode.ToString();
            output.Add(new { Name = name, Input = input, BaselineInput = input, Session = session, Unchanged = unchanged });
        }
        File.WriteAllText(Environment.GetEnvironmentVariable("YGO_PHYSICAL_FIXTURES") ?? throw new InvalidOperationException("Set YGO_PHYSICAL_FIXTURES."),
            JsonSerializer.Serialize(output));
    }

    [TestCase("mill2"), TestCase("banish10")]
    public async Task Profile(string name) {
        SessionState session = await SearchAvailabilityCertificateTest.Example(name);
        SearchCountingDiagnostics diagnostics = new();
        WorkBudget budget = new(CalculationWorkPolicy.Interactive) { SearchDiagnostics = diagnostics };
        Stopwatch timer = Stopwatch.StartNew();
        try {
            DrawEffectCalculation execution = new(session.Cards, session.HandSize, budget, physicalSearch: true);
            ExactCalculationResult result = execution.Calculate(session.Combos, session.ComboGroups);
            TestContext.Out.WriteLine($"{name}: {result.Total.Numerator}/{result.Total.Denominator}");
        }
        finally {
            TestContext.Out.WriteLine($"{name}: {timer.Elapsed.TotalMilliseconds} ms, {budget.Spent} work, {budget.PeakEntries} entries, {budget.PeakCells} cells");
            TestContext.Out.WriteLine(JsonSerializer.Serialize(diagnostics.Counts));
            TestContext.Out.WriteLine(JsonSerializer.Serialize(diagnostics.Milliseconds));
        }
    }
}
