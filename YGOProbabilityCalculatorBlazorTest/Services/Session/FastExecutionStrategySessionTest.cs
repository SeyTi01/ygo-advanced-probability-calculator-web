using System.Text.Json.Nodes;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Session;
using YGOProbabilityCalculatorBlazorTest.Fixtures;

namespace YGOProbabilityCalculatorBlazorTest.Services.Session;

public sealed class FastExecutionStrategySessionTest {
    [TestCase(8), TestCase(9), TestCase(10)]
    public async Task OldModesKeepTheirMeaning(int version) {
        foreach (CalculationModel mode in Enum.GetValues<CalculationModel>()) {
            SessionState loaded = await SessionFixture.CreateCodec().LoadSessionAsync($$"""{"SchemaVersion":{{version}},"CalculationModel":"{{mode}}"}""");
            Assert.That(loaded.CalculationModel, Is.EqualTo(mode));
            Assert.That(loaded.FastExecutionStrategy, Is.EqualTo(FastExecutionStrategy.Progressive));
        }
        Assert.That(new SessionState().FastExecutionStrategy, Is.EqualTo(FastExecutionStrategy.Progressive));
    }

    [TestCase(FastExecutionStrategy.Progressive), TestCase(FastExecutionStrategy.FastOnly)]
    public async Task ShareAndRecoveryPreserveStrategyPlansAndWireIdentity(FastExecutionStrategy strategy) {
        SessionState source = new() {
            CalculationModel = CalculationModel.Fast, FastExecutionStrategy = strategy, HandSize = 2,
            Cards = [new([], 2, id: "d", drawCount: 2, preDrawRemovalCount: 1)],
            DrawActivationPriority = new(DrawActivationMode.CustomPriority, ["d"])
        };
        string json = SessionFixture.CreateCodec().SerializeSession(source);
        string link = SessionShareCodec.CreateLink("https://example.invalid/", json);
        SessionState loaded = await SessionFixture.CreateCodec().LoadSessionAsync(SessionShareCodec.Decode(new Uri(link).Fragment));
        Assert.That(SessionFixture.CreateCodec().SerializeSession(loaded), Is.EqualTo(json));
        Assert.That(loaded.FastExecutionStrategy, Is.EqualTo(strategy));
        Assert.That(loaded.Cards[0].EffectPlan, Is.EqualTo(source.Cards[0].EffectPlan));
        CalculationSnapshot snapshot = CalculationSnapshot.Capture(loaded.Cards, [], 2, [], options: new(loaded.DrawActivationPriority));
        Assert.That(JsonNode.Parse(snapshot.Json)!["Options"]!["Mode"]!.GetValue<int>(), Is.Zero);
        Assert.That(snapshot.Json, Does.Not.Contain("FastExecutionStrategy"));
    }

    [TestCase("null")]
    [TestCase("1")]
    [TestCase("\"Unknown\"")]
    [TestCase("\"1\"")]
    public void InvalidStrategiesAreRejected(string value) {
        Assert.ThrowsAsync<InvalidOperationException>(() => SessionFixture.CreateCodec().LoadSessionAsync(
            $$"""{"SchemaVersion":11,"CalculationModel":"Fast","FastExecutionStrategy":{{value}}}"""));
    }

    [TestCase("{\"SchemaVersion\":11,\"CalculationModel\":\"Fast\"}")]
    [TestCase("{\"SchemaVersion\":11,\"CalculationModel\":\"Exact\",\"FastExecutionStrategy\":\"FastOnly\"}")]
    [TestCase("{\"SchemaVersion\":11,\"FastExecutionStrategy\":\"Progressive\",\"fastexecutionstrategy\":\"FastOnly\"}")]
    [TestCase("{\"SchemaVersion\":10,\"FastExecutionStrategy\":\"FastOnly\"}")]
    public void MissingDuplicateAndContradictoryStrategiesAreRejected(string json) {
        Assert.ThrowsAsync<InvalidOperationException>(() => SessionFixture.CreateCodec().LoadSessionAsync(json));
    }
}
