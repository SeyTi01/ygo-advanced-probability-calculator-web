using Microsoft.JSInterop;
using Moq;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Session;

namespace YGOProbabilityCalculatorBlazorTest.Services.Session;

public sealed class NumericRequirementsSessionTest {
    [Test]
    public async Task NumericComboAndSimulationRangesSurviveSessionAndLegacyMigration() {
        SessionService codec = new(Mock.Of<IJSRuntime>(), new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());
        SessionState original = new() {
            Cards = [new([], name: "Source", drawCount: 1, postDrawDiscardCount: 1,
                postDrawSelector: CardSelector.ForNumericRange(new(NumericPropertyKind.Link, 2, null)))],
            Combos = [new([new(new NumericPropertyRange(NumericPropertyKind.Rank, 3, 7).ToCategory(), 2, 0, RequirementMaximumMode.HandSize)])]
        };
        string json = codec.SerializeSession(original);
        SessionState loaded = await codec.LoadSessionAsync(json);
        Assert.That(loaded.SchemaVersion, Is.EqualTo(12));
        Assert.That(codec.SerializeSession(loaded), Is.EqualTo(json));
        Assert.That(loaded.Combos[0].Categories[0].Selector.NumericRange, Is.EqualTo(new NumericPropertyRange(NumericPropertyKind.Rank, 3, 7)));
        Assert.That(loaded.Cards[0].PostDrawSelector.NumericRange, Is.EqualTo(new NumericPropertyRange(NumericPropertyKind.Link, 2, null)));
        SessionState legacy = await codec.LoadSessionAsync("{\"SchemaVersion\":11,\"CalculationModel\":\"Fast\",\"Cards\":[],\"Combos\":[],\"FastExecutionStrategy\":\"FastOnly\"}");
        Assert.That(legacy.SchemaVersion, Is.EqualTo(12));
        Assert.That(legacy.FastExecutionStrategy.ToString(), Is.EqualTo("FastOnly"));
    }
}
