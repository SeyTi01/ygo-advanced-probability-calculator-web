using Microsoft.JSInterop;
using Moq;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Session;

namespace YGOProbabilityCalculatorBlazorTest.Services.Session;

public sealed class NumericRequirementsSessionTest {
    [Test]
    public async Task NumericCategoryRulesRoundTripWithoutAssigningSyntheticMetadataToCards() {
        SessionService codec = new(Mock.Of<IJSRuntime>(), new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());
        CardSelector range = new NumericPropertyRange(NumericPropertyKind.Level, 3, 5).ToSelector();
        CardSelector legacy = CardSelector.AllOf(["metadata:level:4", "metadata:kind:spell"]);
        CategoryBase role = new("Numeric role", rule: CardSelector.AnyOfSelectors([range, legacy]));
        Card card = new([new("Monster", CategorySource.Metadata, "kind:monster"), new("Level 4", CategorySource.Metadata, "level:4")]);
        SessionState original = new() { Categories = [role], Cards = [card] };
        string json = codec.SerializeSession(original);
        SessionState loaded = await codec.LoadSessionAsync(json);
        Assert.That(codec.SerializeSession(loaded), Is.EqualTo(json));
        Assert.That(loaded.Categories[0].Rule, Is.EqualTo(role.Rule));
        Assert.That(CategoryMembership.Materialize(loaded.Cards, loaded.Categories)[0].Categories, Does.Contain(role));
        Assert.That(loaded.Cards[0].Categories, Does.Not.Contain(role));
        Assert.That(loaded.Cards[0].Categories.Any(property => property.MetadataKey?.StartsWith("numeric:") == true), Is.False);
        Assert.Throws<ArgumentException>(() => new CategoryBase("Invalid", rule: CardSelector.For(new NumericPropertyRange(NumericPropertyKind.Level, 3, 5).ToCategory())));
    }

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
        Assert.That(loaded.SchemaVersion, Is.EqualTo(SessionState.CurrentSchemaVersion));
        Assert.That(codec.SerializeSession(loaded), Is.EqualTo(json));
        Assert.That(loaded.Combos[0].Categories[0].Selector.NumericRange, Is.EqualTo(new NumericPropertyRange(NumericPropertyKind.Rank, 3, 7)));
        Assert.That(loaded.Cards[0].PostDrawSelector.NumericRange, Is.EqualTo(new NumericPropertyRange(NumericPropertyKind.Link, 2, null)));
        SessionState legacy = await codec.LoadSessionAsync("{\"SchemaVersion\":11,\"CalculationModel\":\"Fast\",\"Cards\":[],\"Combos\":[],\"FastExecutionStrategy\":\"FastOnly\"}");
        Assert.That(legacy.SchemaVersion, Is.EqualTo(SessionState.CurrentSchemaVersion));
        Assert.That(legacy.FastExecutionStrategy.ToString(), Is.EqualTo("FastOnly"));
    }
}
