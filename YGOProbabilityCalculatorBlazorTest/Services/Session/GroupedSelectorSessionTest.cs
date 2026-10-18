using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.JSInterop;
using Moq;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;
using YGOProbabilityCalculatorBlazor.Services.Session;
using YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.Session;

public sealed class GroupedSelectorSessionTest {
    private static SessionService Codec() => new(Mock.Of<IJSRuntime>(), new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());

    [Test]
    public async Task SchemaSixSavedPlanAndProvenanceStayAuthoritativeUntilExplicitRestore() {
        Card source = SearchEffectsTest.Preset(92221402);
        EffectProfile oldProfile = source.EffectProfile with { Revision = 1, Support = EffectSupport.Partial };
        Card saved = source.WithEffect(source.EffectPlan with { Search = new(SelectorExpressionTest.Old) }, oldProfile);
        JsonObject raw = JsonNode.Parse(Codec().SerializeSession(new() { Cards = [saved], HandSize = 1 }))!.AsObject();
        raw["SchemaVersion"] = 6;
        raw.Remove("FastExecutionStrategy");
        raw.Remove("CalculationModel");
        SessionState loaded = await Codec().LoadSessionAsync(raw.ToJsonString());
        Card retained = loaded.Cards.Single();
        Assert.That(retained.EffectPlan, Is.EqualTo(saved.EffectPlan));
        Assert.That(retained.EffectProfile, Is.EqualTo(oldProfile));
        Assert.That(CardEffectRegistry.Default.Describe(retained).Support, Is.EqualTo(EffectSupport.Partial));
        Assert.That(retained.Search!.Matches(SelectorExpressionTest.Target(14)), Is.False);
        Card restored = CardEffectRegistry.Default.RestoreBuiltIn(retained);
        Assert.That(restored.Search!.Matches(SelectorExpressionTest.Target(14)), Is.True);
        Assert.That(restored.EffectProfile.Support, Is.EqualTo(EffectSupport.ModeledWithinRules));
        SessionState roundTrip = await Codec().LoadSessionAsync(Codec().SerializeSession(loaded));
        Assert.That(roundTrip.Cards.Single().EffectPlan, Is.EqualTo(saved.EffectPlan));
        Assert.That(roundTrip.Cards.Single().EffectProfile, Is.EqualTo(oldProfile));
    }

    [Test]
    public async Task GroupedSearchCostRemovalShareRecoveryAndWorkerKeepExactExpression() {
        Card source = SearchEffectsTest.Preset(92221402);
        Card target = SelectorExpressionTest.Target();
        Card draw = new([], id: "draw", drawCount: 1, postDrawDiscardCount: 1,
            postDrawSelector: SelectorExpressionTest.Compact, preDrawRemovalCount: 1, preDrawSelector: SelectorExpressionTest.Compact);
        SessionState state = new() { Cards = [source, target, draw, new([], 3, id: "blank")], HandSize = 2,
            Combos = [new([], cards: [new(target.Id, 1, 2)])] };
        string serialized = Codec().SerializeSession(state);
        Assert.That(JsonDocument.Parse(serialized).RootElement.GetProperty("SchemaVersion").GetInt32(), Is.EqualTo(SessionState.CurrentSchemaVersion));
        string fragment = new Uri(SessionShareCodec.CreateLink("https://example.invalid/", serialized)).Fragment;
        foreach (string channel in new[] { serialized, SessionShareCodec.Decode(fragment) }) {
            SessionState loaded = await Codec().LoadSessionAsync(channel);
            Assert.That(Codec().SerializeSession(loaded), Is.EqualTo(serialized));
            Assert.That(loaded.Cards[0].Search!.Selector, Is.EqualTo(SelectorExpressionTest.Compact));
            Assert.That(loaded.Cards[2].PreDrawSelector, Is.EqualTo(SelectorExpressionTest.Compact));
            Assert.That(loaded.Cards[2].PostDrawSelector, Is.EqualTo(SelectorExpressionTest.Compact));
            CalculationSnapshot snapshot = CalculationSnapshot.Capture(loaded.Cards, loaded.Combos, 2, []);
            CalculationInput input = JsonSerializer.Deserialize(snapshot.Json, CalculationJsonContext.Default.CalculationInput)!;
            Assert.That(input.Cards[0].Search!.Selector, Is.EqualTo(SelectorExpressionTest.Compact));
            Assert.That(CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json)).TotalProbability, Is.InRange(0, 1));
            Assert.That(snapshot.Json, Does.Contain("LevelAtLeast"));
        }
        state.Cards[0] = source.WithSearch(new(CardSelector.AllOfSelectors([SelectorExpressionTest.Compact, CardSelector.LevelAtLeast(6)])));
        CardInfo info = new() { Id = 92221402, Name = "Chaotic Elements" };
        Assert.That(CardEffectRegistry.Default.CreateImportedCard(info, 1, info.Id).Search!.Selector, Is.EqualTo(SelectorExpressionTest.Compact));
        Assert.That(CardEffectRegistry.Default.ApplyForExplicitSelection(new([]), new([]), info).Search!.Selector, Is.EqualTo(SelectorExpressionTest.Compact));
    }
}
