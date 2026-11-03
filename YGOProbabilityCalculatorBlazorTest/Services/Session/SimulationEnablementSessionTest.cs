using System.Text.Json.Nodes;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;
using YGOProbabilityCalculatorBlazor.Services.Session;
using YGOProbabilityCalculatorBlazorTest.Fixtures;

namespace YGOProbabilityCalculatorBlazorTest.Services.Session;

[TestFixture]
public sealed class SimulationEnablementSessionTest {
    [Test]
    public async Task FullSessionRoundTripPreservesCustomAndMaintainedPlansAndEntryState() {
        Card maintained = SearchCardFixtures.Rota().WithSimulationEnabled(false);
        Card custom = new([], drawCount: 2, preDrawRemovalCount: 1, preDrawDeckRemovalCount: 1,
            postDrawDeckMillCount: 1, postDrawDiscardCount: 1, activationLimit: 2, minimumHandSize: 2,
            effectProfile: new(EffectSource.Custom, 123), simulationEnabled: false);
        SessionState session = new() { Cards = [maintained, custom, maintained.Duplicate().WithSimulationEnabled(true)] };
        SessionService codec = SessionFixture.CreateCodec();
        string json = codec.SerializeSession(session);
        SessionState loaded = await codec.LoadSessionAsync(json);
        Assert.That(loaded.SchemaVersion, Is.EqualTo(SessionState.CurrentSchemaVersion), "older readers must reject the new behavior");
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
        SessionService codec = SessionFixture.CreateCodec();
        JsonObject root = JsonNode.Parse(codec.SerializeSession(new() { Cards = [SearchCardFixtures.Rota()] }))!.AsObject();
        root["SchemaVersion"] = 12;
        JsonObject card = root["Cards"]![0]!.AsObject();
        card.Remove("SimulationEnabled");
        if (extension) {
            card["SimulationEnabled"] = false;
        }
        Card loaded = (await codec.LoadSessionAsync(root.ToJsonString())).Cards.Single();
        Assert.That(loaded.SimulationEnabled, Is.True);
        Assert.That(loaded.EffectPlan, Is.EqualTo(SearchCardFixtures.Rota().EffectPlan));
    }

    [TestCase("null")]
    [TestCase("0")]
    [TestCase("\"false\"")]
    [TestCase("[]")]
    [TestCase("false,\"simulationenabled\":true")]
    public void MalformedOrDuplicateFlagsAreRejected(string value) {
        string json = $$"""{"SchemaVersion":13,"CalculationModel":"Fast","FastExecutionStrategy":"Progressive","Cards":[{"Name":"Source","Copies":1,"Categories":[],"SimulationEnabled":{{value}}}]}""";
        Assert.ThrowsAsync<InvalidOperationException>(async () => await SessionFixture.CreateCodec().LoadSessionAsync(json));
    }
}
