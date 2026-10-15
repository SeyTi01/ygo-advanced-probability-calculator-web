using System.Text.Json;
using System.Text.Json.Nodes;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.Session;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture, Explicit("Paired native and optimized WASM mode benchmark fixture export"), NonParallelizable]
public sealed class ThreeModeBenchmark {
    [Test]
    public async Task Export() {
        List<(string Name, SessionState State, CalculationModel Mode)> cases = [];
        SessionState draw = await SearchAvailabilityCertificateTest.Example("one-draw2");
        cases.Add(("opening", draw, CalculationModel.OpeningHand));
        SessionState count = await SearchAvailabilityCertificateTest.Example("one-draw2");
        for (int index = 0; index < count.Cards.Count; index++) {
            count.Cards[index] = count.Cards[index].WithSearch(null);
        }
        cases.Add(("count-state", count, CalculationModel.Fast));
        cases.Add(("search-only", await SearchAvailabilityCertificateTest.Example("plain"), CalculationModel.Fast));
        cases.Add(("zero-cost", draw, CalculationModel.Exact));
        cases.Add(("fast-bound", await SearchAvailabilityCertificateTest.Example("mill2"), CalculationModel.Fast));
        cases.Add(("exact-unsupported", await SearchAvailabilityCertificateTest.Example("mill2"), CalculationModel.Exact));
        cases.Add(("consumed", new() { Cards = ThreeCalculationModesTest.Counterexample("consumed"), HandSize = 1,
            Categories = [ThreeCalculationModesTest.Target], Combos = ThreeCalculationModesTest.Combos, ComboGroups = ThreeCalculationModesTest.Groups }, CalculationModel.Fast));
        cases.Add(("discard", new() { Cards = ThreeCalculationModesTest.Counterexample("discard"), HandSize = 2,
            Categories = [ThreeCalculationModesTest.Target], Combos = ThreeCalculationModesTest.Combos, ComboGroups = ThreeCalculationModesTest.Groups }, CalculationModel.Fast));
        List<object> output = [];
        SessionService codec = new(null!, new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());
        foreach ((string name, SessionState state, CalculationModel mode) in cases) {
            JsonObject input = JsonNode.Parse(CalculationSnapshot.Capture(state.Cards, state.Combos, state.HandSize, state.ComboGroups,
                CalculationWorkPolicy.Interactive, options: new(Mode: mode)).Json)!.AsObject();
            JsonObject baseline = input.DeepClone().AsObject();
            baseline["Options"]!.AsObject().Remove("Mode");
            if (name == "fast-bound") {
                baseline["Options"]!["CertifySearchAvailability"] = true;
            }
            JsonObject session = JsonNode.Parse(codec.SerializeSession(state))!.AsObject();
            session["CalculationModel"] = mode.ToString();
            output.Add(new { Name = name, BaselineInput = baseline, Input = input, Session = session });
        }
        File.WriteAllText(Environment.GetEnvironmentVariable("YGO_MODE_FIXTURES") ?? throw new InvalidOperationException("Set YGO_MODE_FIXTURES."),
            JsonSerializer.Serialize(output));
    }
}
