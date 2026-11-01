using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.JSInterop;
using Moq;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;
using YGOProbabilityCalculatorBlazor.Services.Session;

namespace YGOProbabilityCalculatorBlazorTest.Services.Session;

public sealed class ExcavationSessionTest {
    private static SessionService Codec() => new(Mock.Of<IJSRuntime>(), new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());

    [TestCase(98645731, 3, false)]
    [TestCase(84211599, 6, true)]
    public async Task ImportedPresetAndCustomDisabledSnapshotsSurviveSessionShareAndWorkerTransport(int identity, int count, bool drawRestriction) {
        CardEffectRegistry registry = CardEffectRegistry.Default;
        Card card = registry.CreateImportedCard(new() { Id = 99, CanonicalCardId = identity, Name = "Localized", ArtworkImageIds = [identity, 99] }, 2, 99);
        Assert.That(card.Excavation, Is.EqualTo(new ExcavationEffectPlan(count, drawRestriction)));
        Assert.That(card.ExternalCardId, Is.EqualTo(99));
        Assert.That(card.Search, Is.Null);
        Assert.That(card.DrawCount, Is.Null);
        Assert.That(registry.Describe(card).Capabilities.HasFlag(EffectCapabilities.Excavation), Is.True);
        foreach (Card snapshot in new[] { card, card.WithExcavation(new(2, drawRestriction)).WithSimulationEnabled(false) }) {
            Card copy = snapshot.Duplicate().WithName("Renamed").WithCopies(3).WithCategories([]).WithActive(false).WithActive(true);
            Assert.That(copy.EffectPlan, Is.EqualTo(snapshot.EffectPlan));
            Assert.That(copy.SimulationEnabled, Is.EqualTo(snapshot.SimulationEnabled));
            SessionService codec = Codec();
            string json = codec.SerializeSession(new() { Cards = [snapshot] });
            string link = SessionShareCodec.CreateLink("https://example.invalid/", json);
            foreach (string payload in new[] { json, SessionShareCodec.Decode(new Uri(link).Fragment) }) {
                Card loaded = (await codec.LoadSessionAsync(payload)).Cards.Single();
                Assert.That(loaded.EffectPlan, Is.EqualTo(snapshot.EffectPlan));
                Assert.That(loaded.EffectProfile, Is.EqualTo(snapshot.EffectProfile));
                Assert.That(loaded.SimulationEnabled, Is.EqualTo(snapshot.SimulationEnabled));
            }

            foreach (bool enabled in new[] { false, true }) {
                CalculationSnapshot wire = CalculationSnapshot.Capture([snapshot], [], 1, [], drawEffectsEnabled: enabled);
                CalculationInput input = JsonSerializer.Deserialize(wire.Json, CalculationJsonContext.Default.CalculationInput)!;
                Assert.That(input.Cards.Single().Excavation, Is.EqualTo(enabled && snapshot.SimulationEnabled ? snapshot.Excavation : null));
            }
        }

        Card disabled = card.WithExcavation(null).WithSimulationEnabled(false);
        Assert.That(registry.RestoreBuiltIn(disabled).Excavation, Is.EqualTo(card.Excavation));
        Assert.That(registry.RestoreBuiltIn(disabled).SimulationEnabled, Is.False);
    }

    [Test]
    public async Task OlderSessionNeverAcquiresExcavationFromItsIdentityOrAnIgnoredExtension() {
        SessionService codec = Codec();
        Card historical = new([], name: "Pot of Prosperity", externalCardId: 84211599, drawCount: 2);
        JsonObject root = JsonNode.Parse(codec.SerializeSession(new() { Cards = [historical] }))!.AsObject();
        root["SchemaVersion"] = 13;
        root["Cards"]![0]!["Excavation"] = JsonNode.Parse("""{"Count":6,"RequiresNoPriorEffectDraw":true}""");
        Card loaded = (await codec.LoadSessionAsync(root.ToJsonString())).Cards.Single();
        Assert.That(loaded.Excavation, Is.Null);
        Assert.That(loaded.DrawCount, Is.EqualTo(2));
        Assert.That(loaded.EffectProfile, Is.EqualTo(historical.EffectProfile));
        Assert.That(CardEffectRegistry.Default.RestoreBuiltIn(loaded).Excavation?.Count, Is.EqualTo(6));
    }

    [TestCase("null")]
    [TestCase("{}")]
    [TestCase("{\"Count\":0}")]
    [TestCase("{\"Count\":7}")]
    [TestCase("{\"Count\":3,\"count\":6}")]
    [TestCase("{\"Count\":3,\"RequiresNoPriorEffectDraw\":\"false\"}")]
    [TestCase("{\"Count\":3,\"UnknownFutureOperation\":true}")]
    public void MalformedExcavationSnapshotsAreRejected(string operation) {
        string json = $$"""{"SchemaVersion":14,"CalculationModel":"Fast","FastExecutionStrategy":"Progressive","Cards":[{"Name":"Source","Copies":1,"Categories":[],"Excavation":{{operation}}}]}""";
        Assert.ThrowsAsync<InvalidOperationException>(async () => await Codec().LoadSessionAsync(json));
    }

    [Test]
    public void ExcavationCannotShareDrawSearchOrCostOperations() {
        ExcavationEffectPlan excavation = new(3);
        Assert.Throws<ArgumentException>(() => new Card([], drawCount: 1, excavation: excavation));
        Assert.Throws<ArgumentException>(() => new Card([], search: new(CardSelector.Any), excavation: excavation));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Card([], preDrawRemovalCount: 1, excavation: excavation));
        Card source = new([], excavation: excavation);
        Assert.That(source.WithDrawCount(1).Excavation, Is.Null);
        Assert.That(source.WithSearch(new(CardSelector.Any)).Excavation, Is.Null);
        Assert.That(source.WithExcavation(null).HasEffect, Is.False);
    }
}
