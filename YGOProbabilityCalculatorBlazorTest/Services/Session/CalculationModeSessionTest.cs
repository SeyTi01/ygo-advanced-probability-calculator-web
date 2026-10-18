using System.Text.Json;
using System.Text.Json.Nodes;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Session;

namespace YGOProbabilityCalculatorBlazorTest.Services.Session;

public sealed class CalculationModeSessionTest {
    private static SessionService Codec() => new(null!, new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());

    public static IEnumerable<TestCaseData> LegacyModes() => Enumerable.Range(0, 8)
        .SelectMany(version => new string?[] { null, "true", "false" }.Select(value => new TestCaseData(version, value)));

    [TestCaseSource(nameof(LegacyModes))]
    public async Task EveryOldSchemaMapsOnlyToFastOrOpening(int schema, string? enabled) {
        JsonObject root = new() { ["SchemaVersion"] = schema };
        if (enabled is not null) {
            root["DrawEffectsEnabled"] = JsonNode.Parse(enabled);
        }
        SessionState loaded = await Codec().LoadSessionAsync(root.ToJsonString());
        Assert.That(loaded.FastExecutionStrategy, Is.EqualTo(FastExecutionStrategy.Progressive));
        Assert.That(loaded.CalculationModel, Is.EqualTo(enabled == "false" ? CalculationModel.OpeningHand : CalculationModel.Fast));
        JsonObject saved = JsonNode.Parse(Codec().SerializeSession(loaded))!.AsObject();
        Assert.That(saved["SchemaVersion"]!.GetValue<int>(), Is.EqualTo(SessionState.CurrentSchemaVersion));
        Assert.That(saved.ContainsKey("DrawEffectsEnabled"), Is.False);
    }

    [TestCase(CalculationModel.Fast), TestCase(CalculationModel.Exact), TestCase(CalculationModel.OpeningHand)]
    public async Task SchemaEightShareAndRecoveryRoundTripOneAuthoritativeMode(CalculationModel mode) {
        SessionState state = new() {
            CalculationModel = mode, HandSize = 2, Cards = [new([], 2, id: "d", drawCount: 2, preDrawRemovalCount: 1)],
            DrawActivationPriority = new(DrawActivationMode.CustomPriority, ["d"]),
            CategoryColorIndices = new() { ["custom"] = 4 }
        };
        string json = Codec().SerializeSession(state);
        string shared = SessionShareCodec.CreateLink("https://example.invalid/", json);
        SessionState loaded = await Codec().LoadSessionAsync(SessionShareCodec.Decode(new Uri(shared).Fragment));
        Assert.That(loaded.CalculationModel, Is.EqualTo(mode));
        Assert.That(loaded.Cards[0].EffectPlan, Is.EqualTo(state.Cards[0].EffectPlan));
        Assert.That(loaded.Cards[0].EffectProfile, Is.EqualTo(state.Cards[0].EffectProfile));
        Assert.That(loaded.DrawActivationPriority.CardIds, Is.EqualTo(new[] { "d" }));
        Assert.That(loaded.CategoryColorIndices, Is.EqualTo(state.CategoryColorIndices));
        Assert.That(Codec().SerializeSession(loaded), Is.EqualTo(json));
    }

    [TestCase("{\"SchemaVersion\":8,\"CalculationModel\":999}")]
    [TestCase("{\"SchemaVersion\":8,\"CalculationModel\":\"Configured\"}")]
    [TestCase("{\"SchemaVersion\":8,\"CalculationModel\":\"0\"}")]
    [TestCase("{\"SchemaVersion\":8,\"CalculationModel\":null}")]
    [TestCase("{\"SchemaVersion\":8,\"CalculationModel\":{\"Mode\":\"Exact\"}}")]
    [TestCase("{\"SchemaVersion\":8,\"CalculationModel\":\"Fast\",\"calculationmodel\":\"Exact\"}")]
    [TestCase("{\"SchemaVersion\":8,\"CalculationModel\":\"Fast\",\"DrawEffectsEnabled\":true}")]
    [TestCase("{\"SchemaVersion\":7,\"CalculationModel\":\"Exact\"}")]
    [TestCase("{\"SchemaVersion\":7,\"DrawEffectsEnabled\":null}")]
    [TestCase("{\"SchemaVersion\":7,\"DrawEffectsEnabled\":false,\"draweffectsenabled\":true}")]
    [TestCase("{\"SchemaVersion\":11}")]
    public void UnknownDuplicateAndContradictoryRepresentationsAreRejected(string json) {
        Assert.ThrowsAsync<InvalidOperationException>(() => Codec().LoadSessionAsync(json));
    }
}
