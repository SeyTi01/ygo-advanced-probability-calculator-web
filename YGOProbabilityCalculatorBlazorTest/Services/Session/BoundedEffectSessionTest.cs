using System.Text.Json;
using System.Text.Json.Nodes;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;
using YGOProbabilityCalculatorBlazor.Services.Session;

namespace YGOProbabilityCalculatorBlazorTest.Services.Session;

public sealed class BoundedEffectSessionTest {
    private static SessionService Codec() => new(null!, new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());

    [TestCase("ActivationLimit", "0"), TestCase("ActivationLimit", "61"), TestCase("ActivationLimit", "1.5")]
    [TestCase("ActivationLimit", "true"), TestCase("MinimumHandSize", "-1"), TestCase("MinimumHandSize", "61")]
    public void InvalidActivationFieldsAreRejectedAsSessionErrors(string field, string value) {
        Card source = CardEffectRegistry.Default.RestoreBuiltIn(new([], externalCardId: 78058681));
        JsonObject root = JsonNode.Parse(Codec().SerializeSession(new() { Cards = [source] }))!.AsObject();
        root["Cards"]![0]![field] = JsonNode.Parse(value);
        Assert.ThrowsAsync<InvalidOperationException>(() => Codec().LoadSessionAsync(root.ToJsonString()));
    }

    [Test]
    public void ModeListsAreImmutableAndRejectInvalidUnionLimitsAndUnboundedCount() {
        CardSelector a = CardSelector.CatalogIdentity(1), b = CardSelector.CatalogIdentity(2);
        List<SearchEffectMode> mutable = [new(a, 1), new(b, 1)];
        SearchEffectPlan search = new(CardSelector.AnyOfSelectors([a, b]), Modes: mutable);
        mutable.Clear();
        Assert.That(search.Modes, Has.Count.EqualTo(2));
        SearchEffectPlan copy = search with { Modes = mutable };
        mutable.Add(new(a, 2));
        Assert.That(copy.Modes, Is.Empty);
        Assert.Throws<ArgumentException>(() => new Card([], search: copy));
        Assert.Throws<ArgumentException>(() => new Card([], search: search with { Selector = a }));
        Assert.Throws<ArgumentException>(() => new Card([], search: new(a, Modes: [new(a, 0)])));
        Assert.Throws<ArgumentException>(() => new Card([], search: new(a, Modes: Enumerable.Repeat(new SearchEffectMode(a, 1), 9).ToArray())));
        Assert.That(search, Is.EqualTo(JsonSerializer.Deserialize<SearchEffectPlan>(JsonSerializer.Serialize(search))));
    }

    [Test]
    public async Task SchemaEightPreservesAllModesAndExplicitCustomOverridesWithoutRefreshingDefinitions() {
        Card old = new([], drawCount: 1, drawOncePerTurn: false, externalCardId: 93946239,
            effectProfile: new(EffectSource.BuiltIn, 93946239, 1, "Draw 1", EffectSupport.Partial, "Minimum hand size is not checked."));
        foreach (CalculationModel mode in Enum.GetValues<CalculationModel>()) {
            if (mode is not (CalculationModel.Fast or CalculationModel.Exact or CalculationModel.OpeningHand)) {
                continue;
            }
            JsonObject root = JsonNode.Parse(Codec().SerializeSession(new() { Cards = [old, old.Duplicate().WithDrawCount(2)], CalculationModel = mode }))!.AsObject();
            root["SchemaVersion"] = 8;
            root.Remove("FastExecutionStrategy");
            SessionState saved = await Codec().LoadSessionAsync(root.ToJsonString());
            Assert.That(saved.SchemaVersion, Is.EqualTo(SessionState.CurrentSchemaVersion));
            Assert.That(saved.CalculationModel, Is.EqualTo(mode));
            Assert.That(saved.Cards[0].EffectPlan, Is.EqualTo(old.EffectPlan));
            Assert.That(saved.Cards[0].EffectProfile, Is.EqualTo(old.EffectProfile));
            Assert.That(saved.Cards[1].DrawCount, Is.EqualTo(2));
            Assert.That(saved.Cards[1].MinimumHandSize, Is.Zero);
        }
    }
}
