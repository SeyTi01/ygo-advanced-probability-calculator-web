using System.Text.Json.Nodes;
using Microsoft.JSInterop;
using Moq;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;
using YGOProbabilityCalculatorBlazor.Services.Session;
using YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.Session;

[TestFixture]
public sealed class SimulationEnablementSessionTest {
    private static SessionService Codec() => new(Mock.Of<IJSRuntime>(), new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());

    [Test]
    public async Task FullSessionRoundTripPreservesCustomAndMaintainedPlansAndEntryState() {
        Card maintained = SearchEffectsTest.Rota().WithSimulationEnabled(false);
        Card custom = new([], drawCount: 2, preDrawRemovalCount: 1, preDrawDeckRemovalCount: 1,
            postDrawDeckMillCount: 1, postDrawDiscardCount: 1, activationLimit: 2, minimumHandSize: 2,
            effectProfile: new(EffectSource.Custom, 123), simulationEnabled: false);
        SessionState session = new() { Cards = [maintained, custom, maintained.Duplicate().WithSimulationEnabled(true)] };
        SessionService codec = Codec();
        string json = codec.SerializeSession(session);
        SessionState loaded = await codec.LoadSessionAsync(json);
        Assert.That(loaded.SchemaVersion, Is.EqualTo(13), "older readers must reject the new behavior");
        Assert.That(codec.SerializeSession(loaded), Is.EqualTo(json));
        for (int index = 0; index < session.Cards.Count; index++) {
            Assert.That(loaded.Cards[index].SimulationEnabled, Is.EqualTo(session.Cards[index].SimulationEnabled));
            Assert.That(loaded.Cards[index].EffectPlan, Is.EqualTo(session.Cards[index].EffectPlan));
            Assert.That(loaded.Cards[index].EffectProfile, Is.EqualTo(session.Cards[index].EffectProfile));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task OlderReaderMeaningDefaultsToEnabledEvenWithAnUnknownExtension(bool extension) {
        SessionService codec = Codec();
        JsonObject root = JsonNode.Parse(codec.SerializeSession(new() { Cards = [SearchEffectsTest.Rota()] }))!.AsObject();
        root["SchemaVersion"] = 12;
        JsonObject card = root["Cards"]![0]!.AsObject();
        card.Remove("SimulationEnabled");
        if (extension) {
            card["SimulationEnabled"] = false;
        }
        Card loaded = (await codec.LoadSessionAsync(root.ToJsonString())).Cards.Single();
        Assert.That(loaded.SimulationEnabled, Is.True);
        Assert.That(loaded.EffectPlan, Is.EqualTo(SearchEffectsTest.Rota().EffectPlan));
    }

    [TestCase("null")]
    [TestCase("0")]
    [TestCase("\"false\"")]
    [TestCase("[]")]
    [TestCase("false,\"simulationenabled\":true")]
    public void MalformedOrDuplicateFlagsAreRejected(string value) {
        string json = $$"""{"SchemaVersion":13,"CalculationModel":"Fast","FastExecutionStrategy":"Progressive","Cards":[{"Name":"Source","Copies":1,"Categories":[],"SimulationEnabled":{{value}}}]}""";
        Assert.ThrowsAsync<InvalidOperationException>(async () => await Codec().LoadSessionAsync(json));
    }

    [Test]
    public void CardReconstructionsRetainTheSwitchAndRestoringBuiltInDoesNotEnableIt() {
        CategoryBase role = new("Role");
        CategoryBase property = new("Spell", CategorySource.Metadata, "kind:spell");
        Card card = SearchEffectsTest.Rota().WithSimulationEnabled(false);
        Card[] changes = [card.Duplicate(), card.WithName("Renamed"), card.WithCopies(3), card.WithActive(false),
            card.WithCategories([role]), card.WithManualMetadataCategory(property),
            card.WithManualMetadataCategory(property).WithoutManualMetadataCategory(property.MetadataKey!),
            card.WithObjectiveMetadata([property], 32807846), card.WithOncePerTurn(true),
            card.WithSearch(new(CardSelector.For(role), false)),
            CardEffectRegistry.Default.RestoreBuiltIn(card.WithSearch(new(CardSelector.Any, false)))];
        Assert.That(changes.All(changed => !changed.SimulationEnabled), Is.True);
        Assert.That(changes[0].Id, Is.Not.EqualTo(card.Id));
        Assert.That(changes[0].EffectPlan, Is.EqualTo(card.EffectPlan));
        Assert.That(changes[^1].EffectProfile, Is.EqualTo(card.EffectProfile));
    }
}
