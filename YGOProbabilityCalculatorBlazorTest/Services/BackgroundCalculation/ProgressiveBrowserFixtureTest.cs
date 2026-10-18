using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.Session;
using YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.BackgroundCalculation;

[TestFixture]
public sealed class ProgressiveBrowserFixtureTest {
    [Test, Explicit("Export native reference rows and sessions for the production progressive browser smoke")]
    public async Task Export() {
        SessionService codec = new(null!, new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());
        SessionState source = await SelectiveSurvivorTest.Source();
        SessionState control = new() {
            Cards = ThreeCalculationModesTest.Counterexample("banish"),
            Categories = [ThreeCalculationModesTest.Target],
            Combos = ThreeCalculationModesTest.Combos,
            ComboGroups = ThreeCalculationModesTest.Groups,
            HandSize = 2
        };
        List<object> output = [];
        foreach ((string name, SessionState session) in new[] { ("source", source), ("control", control) }) {
            CalculationSnapshot snapshot = CalculationSnapshot.Capture(session.Cards, session.Combos, session.HandSize,
                session.ComboGroups, CalculationWorkPolicy.Interactive, options: new(session.DrawActivationPriority, Mode: CalculationModel.Fast));
            ProbabilityCalculationResult initial = CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json), CalculationModel.Fast);
            Assert.That(initial.Contract!.Accuracy, Is.EqualTo(CalculationAccuracy.Bounded));
            if (name == "source") {
                Assert.That(initial.SearchAvailability!.Total!.Lower, Is.EqualTo(new ProbabilityFraction("61422732523", "68906597760")));
                Assert.That(initial.SearchAvailability.Total.Upper, Is.EqualTo(new ProbabilityFraction("63931285207", "68906597760")));
            }
            CalculationSnapshot exact = CalculationSnapshot.Capture(session.Cards, session.Combos, session.HandSize,
                session.ComboGroups, CalculationWorkPolicy.Interactive, options: new(session.DrawActivationPriority, Mode: CalculationModel.Exact));
            output.Add(new {
                Name = name,
                Session = JsonSerializer.Deserialize<JsonElement>(codec.SerializeSession(session)),
                Input = JsonSerializer.Deserialize<JsonElement>(snapshot.Json),
                Initial = initial,
                Exact = CalculationWire.ReadResult(CalculationWire.Execute(exact.Json), CalculationModel.Exact)
            });
        }
        File.WriteAllText(Environment.GetEnvironmentVariable("YGO_PROGRESSIVE_FIXTURES")
            ?? throw new InvalidOperationException("Set YGO_PROGRESSIVE_FIXTURES."), JsonSerializer.Serialize(output));
    }
}
