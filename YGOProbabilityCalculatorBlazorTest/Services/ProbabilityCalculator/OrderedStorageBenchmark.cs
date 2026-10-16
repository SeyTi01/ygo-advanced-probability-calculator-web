using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.Session;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture, Explicit("Ordered storage pressure fixture export"), NonParallelizable]
public sealed class OrderedStorageBenchmark {
    [Test]
    public void Export() {
        SessionService codec = new(null!, new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());
        List<object> output = [];
        foreach (string variant in new[] { "base", "copies", "priority", "eleven" }) {
            SessionState state = OrderedTerminalAggregationTest.Pressure(variant, roles: variant == "eleven" ? 11 : 10);
            output.Add(new {
                Name = "ordered-storage-" + variant,
                Physical = false,
                Input = JsonSerializer.Deserialize<JsonElement>(CalculationSnapshot.Capture(state.Cards, state.Combos,
                    state.HandSize, state.ComboGroups, CalculationWorkPolicy.Interactive, options: new(state.DrawActivationPriority)).Json),
                Session = JsonSerializer.Deserialize<JsonElement>(codec.SerializeSession(state))
            });
        }
        File.WriteAllText(Environment.GetEnvironmentVariable("YGO_ORDERED_STORAGE_FIXTURES")
            ?? throw new InvalidOperationException("Set YGO_ORDERED_STORAGE_FIXTURES."), JsonSerializer.Serialize(output));
    }
}
