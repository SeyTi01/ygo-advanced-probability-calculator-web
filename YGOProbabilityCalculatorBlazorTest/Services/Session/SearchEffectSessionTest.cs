using System.Text.Json;
using Microsoft.JSInterop;
using Moq;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;
using YGOProbabilityCalculatorBlazor.Services.DeckImport;
using YGOProbabilityCalculatorBlazor.Services.Session;
using YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.Session;

public sealed class SearchEffectSessionTest {
    private static SessionService Codec() => new(Mock.Of<IJSRuntime>(), new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());

    [TestCase(0), TestCase(1), TestCase(2), TestCase(3), TestCase(4), TestCase(5)]
    public async Task OlderSchemasNeverInjectSearchOrExecutePreviouslyIgnoredFields(int schema) {
        string json = $$$$"""{"SchemaVersion":{{{{schema}}}},"Cards":[{"Id":"old","Name":"Reinforcement of the Army","ExternalCardId":32807846,"Copies":1,"Categories":[{"Name":"VS Starter"}],"DrawCount":1,"Search":{"Selector":{"Kind":"Any","Categories":[]}}}]}""";
        SessionState loaded = await Codec().LoadSessionAsync(json);
        Assert.That(loaded.Cards[0].Search, Is.Null);
        Assert.That(loaded.Cards[0].DrawCount, Is.EqualTo(schema < 4 ? null : (int?)1));
        Assert.That(loaded.Cards[0].Categories.Any(category => category.Name == "VS Starter"), Is.True);
        Assert.That(loaded.SchemaVersion, Is.EqualTo(SessionState.CurrentSchemaVersion));
    }

    [Test]
    public async Task SavedLegacyProxyRemainsUnchangedUntilExplicitRestore() {
        SessionState legacy = await SearchEffectsTest.LoadExample("example_session_state.json");
        Card rota = legacy.Cards.Single(card => card.ExternalCardId == 32807846);
        Assert.That(rota.Search, Is.Null);
        Assert.That(rota.ManualMetadataCategoryKeys, Does.Contain("attribute:fire"));
        Card restored = CardEffectRegistry.Default.RestoreBuiltIn(rota);
        Assert.That(restored.Search, Is.Not.Null);
        Assert.That(restored.Categories, Is.EqualTo(rota.Categories));
    }

    [TestCase(32807846), TestCase(80181649), TestCase(92221402)]
    public async Task MaintainedCustomDisabledSnapshotsCloneShareAndWorkerRoundTrip(int identity) {
        Card maintained = SearchEffectsTest.Preset(identity);
        Card custom = maintained.WithSearch(new(CardSelector.For(new CategoryBase("Role")), false)).WithOncePerTurn(true);
        foreach (Card card in new[] { maintained, custom, maintained.WithSearch(null), custom.WithDrawCount(2) }) {
            Card copy = card.Duplicate().WithName("Renamed").WithCopies(2).WithActive(false).WithActive(true)
                .WithCategories(card.Categories).WithObjectiveMetadata(card.Categories, identity);
            Assert.That(copy.Id, Is.Not.EqualTo(card.Id));
            Assert.That(copy.EffectPlan, Is.EqualTo(card.EffectPlan));
            SessionState state = new() { Cards = [card], Categories = [new("Role")] };
            string json = Codec().SerializeSession(state);
            Assert.That(JsonDocument.Parse(json).RootElement.GetProperty("SchemaVersion").GetInt32(), Is.EqualTo(SessionState.CurrentSchemaVersion));
            string link = SessionShareCodec.CreateLink("https://example.invalid/", json);
            foreach (string saved in new[] { json, SessionShareCodec.Decode(new Uri(link).Fragment) }) {
                SessionState loaded = await Codec().LoadSessionAsync(saved);
                Assert.That(loaded.Cards[0].EffectPlan, Is.EqualTo(card.EffectPlan));
                Assert.That(loaded.Cards[0].EffectProfile, Is.EqualTo(card.EffectProfile));
            }
            foreach (bool enabled in new[] { true, false }) {
                CalculationSnapshot snapshot = CalculationSnapshot.Capture([card, SearchEffectsTest.Warrior()], [], 1, [], drawEffectsEnabled: enabled);
                CalculationInput wire = JsonSerializer.Deserialize(snapshot.Json, CalculationJsonContext.Default.CalculationInput)!;
                Assert.That(wire.Cards[0].Search, Is.EqualTo(enabled ? card.Search : null));
                Assert.That(wire.Cards[0].DrawCount, Is.EqualTo(enabled ? card.DrawCount : null));
                Assert.DoesNotThrow(() => CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json)));
            }
        }
        Assert.That(CardEffectRegistry.Default.RestoreBuiltIn(custom).EffectPlan, Is.EqualTo(maintained.EffectPlan));
    }

    [Test]
    public void ImportAndExplicitSelectionUseCanonicalIdentityButSameSelectionPreservesCustomPlan() {
        CardInfo info = new() { Id = 32807846, Name = "Arbitrary localized name" };
        CardEffectRegistry registry = CardEffectRegistry.Default;
        Card imported = registry.CreateImportedCard(info, 2, info.Id);
        Assert.That(imported.Search, Is.EqualTo(SearchEffectsTest.Rota().Search));
        Card ordinary = new([], id: "stable");
        Card selected = registry.ApplyForExplicitSelection(ordinary, ordinary, info);
        Assert.That(selected.Id, Is.EqualTo("stable"));
        Assert.That(selected.Search, Is.EqualTo(imported.Search));
        Card disabled = selected.WithSearch(null);
        Assert.That(registry.ApplyForExplicitSelection(disabled, disabled, info).Search, Is.Null);
        Assert.That(registry.Describe(imported).Capabilities, Is.EqualTo(EffectCapabilities.Search));
    }

    [Test]
    public void InvalidAndFutureSnapshotsFailExplicitly() {
        Assert.ThrowsAsync<InvalidOperationException>(async () => await Codec().LoadSessionAsync("{\"SchemaVersion\":8}"));
        Assert.ThrowsAsync<InvalidOperationException>(async () => await Codec().LoadSessionAsync("""
            {"SchemaVersion":6,"Cards":[{"Name":"bad","Categories":[],"Copies":1,"DrawCount":1,"Search":{"Selector":{"Kind":"Any","Categories":[]}}}]}
            """));
    }
    [TestCase(false, false), TestCase(false, true), TestCase(true, false), TestCase(true, true)]
    public async Task OldSchemaSixPlansKeepProvenanceAndDefaultToUnrestricted(bool objective, bool oldDrawFlag) {
        string json = $$$$"""
            {"SchemaVersion":6,"Cards":[{"Id":"saved","Name":"Legacy","Copies":2,"Categories":[],"ExternalCardId":80181649,"DrawOncePerTurn":{{{{oldDrawFlag.ToString().ToLowerInvariant()}}}},
            "Search":{"Selector":{"Kind":"Any","Categories":[]},"ObjectiveMetadataOnly":{{{{objective.ToString().ToLowerInvariant()}}}}}}]}
            """;
        Card source = (await Codec().LoadSessionAsync(json)).Cards[0];
        Assert.That(source.Search!.ObjectiveMetadataOnly, Is.EqualTo(objective));
        Assert.That(source.OncePerTurn, Is.False, "The old draw-only flag was never applied to searches.");
        Card restored = CardEffectRegistry.Default.RestoreBuiltIn(source);
        Assert.That(restored.OncePerTurn, Is.True);
        Assert.That(restored.Search.ObjectiveMetadataOnly, Is.True);
        Card roundTrip = (await Codec().LoadSessionAsync(Codec().SerializeSession(new() { Cards = [source] }))).Cards[0];
        Assert.That(roundTrip.Search, Is.EqualTo(source.Search));
    }

    [TestCase("example_session_state.json"), TestCase("rota_search_example_session_state.json")]
    public async Task HistoricalExamplesNeverInjectNewPresetsOrRemoveManualProxies(string fixture) {
        SessionState state = await SearchEffectsTest.LoadExample(fixture);
        foreach (int identity in new[] { 80181649, 92221402 }) {
            Card card = state.Cards.Single(card => card.ExternalCardId == identity);
            Assert.That(card.Search, Is.Null);
            Assert.That(card.Categories.Any(category => category.Name == "K9 Starter"), Is.True);
            Assert.That(card.ManualMetadataCategoryKeys, Is.EquivalentTo(new[] { "attribute:dark", "attribute:earth", "level:5" }));
        }
    }

    [TestCase(80181649), TestCase(92221402)]
    public void NewPresetImportUsesCanonicalIdAndKeepsExplicitOverride(int identity) {
        CardInfo artwork = new() { Id = 99, CanonicalCardId = identity, ArtworkImageIds = [identity, 99], Name = "Localized" };
        CardEffectRegistry registry = CardEffectRegistry.Default;
        Card imported = registry.CreateImportedCard(artwork, 2, artwork.Id);
        Assert.That(imported.ExternalCardId, Is.EqualTo(99));
        Assert.That(imported.EffectProfile.CanonicalCardId, Is.EqualTo(identity));
        Assert.That(imported.OncePerTurn, Is.True);
        Assert.That(imported.Search, Is.EqualTo(SearchEffectsTest.Preset(identity).Search));
        Card custom = imported.WithOncePerTurn(false);
        Assert.That(registry.ApplyForExplicitSelection(custom, custom, artwork).OncePerTurn, Is.False);
        Assert.That(registry.RestoreBuiltIn(custom).OncePerTurn, Is.True);
    }

    [Test]
    public void PinnedFingerprintsObserveSharedSearchAllowance() {
        Card source = SearchEffectsTest.Preset(80181649);
        string Fingerprint(Card card) => PinnedCalculationContext.Capture(0, 2, [card], [], [], _ => Guid.Empty, _ => Guid.Empty).DeckDefinition;
        string original = Fingerprint(source);
        Assert.That(Fingerprint(source.WithOncePerTurn(false)), Is.Not.EqualTo(original));
        Assert.That(Fingerprint(source.WithEffect(source.EffectPlan, source.EffectProfile with { CanonicalCardId = 92221402 })), Is.EqualTo(original), "Provenance cannot change per-entry activation semantics.");
        Assert.That(Fingerprint(source.Duplicate()), Is.Not.EqualTo(original));
    }

    [TestCase("null"), TestCase("1"), TestCase("\"yes\"")]
    public void InvalidSharedActivationFlagsFailExplicitly(string value) {
        string json = "{\"SchemaVersion\":6,\"Cards\":[{\"Name\":\"bad\",\"Categories\":[],\"Copies\":1,\"OncePerTurn\":" + value + "}]}";
        Assert.ThrowsAsync<InvalidOperationException>(async () => await Codec().LoadSessionAsync(json));
    }

    [Test]
    public async Task SharedFlagCanConfigureDrawsWithoutChangingLegacyDrawSemantics() {
        foreach (bool once in new[] { false, true }) {
            Card draw = new([], 2, drawCount: 1, oncePerTurn: once);
            string json = Codec().SerializeSession(new() { Cards = [draw] });
            Card loaded = (await Codec().LoadSessionAsync(json)).Cards[0];
            Assert.That(loaded.EffectPlan, Is.EqualTo(draw.EffectPlan));
            Assert.That(loaded.DrawOncePerTurn, Is.EqualTo(once));
            Assert.That(draw.WithSearch(null).OncePerTurn, Is.EqualTo(once));
        }
    }

}
