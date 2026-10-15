using System.Text.Json;
using Microsoft.JSInterop;
using Moq;
using YGOProbabilityCalculatorBlazor.Models;
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
        Assert.That(loaded.SchemaVersion, Is.EqualTo(6));
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

    [Test]
    public async Task MaintainedCustomDisabledSnapshotsCloneShareAndWorkerRoundTrip() {
        Card maintained = SearchEffectsTest.Rota();
        Card custom = maintained.WithSearch(new(CardSelector.For(new CategoryBase("Role")), false));
        foreach (Card card in new[] { maintained, custom, maintained.WithSearch(null), custom.WithDrawCount(2) }) {
            Card copy = card.Duplicate().WithName("Renamed").WithCopies(2).WithActive(false).WithActive(true)
                .WithCategories(card.Categories).WithObjectiveMetadata(card.Categories, 32807846);
            Assert.That(copy.Id, Is.Not.EqualTo(card.Id));
            Assert.That(copy.EffectPlan, Is.EqualTo(card.EffectPlan));
            SessionState state = new() { Cards = [card], Categories = [new("Role")] };
            string json = Codec().SerializeSession(state);
            Assert.That(JsonDocument.Parse(json).RootElement.GetProperty("SchemaVersion").GetInt32(), Is.EqualTo(6));
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
        Assert.ThrowsAsync<InvalidOperationException>(async () => await Codec().LoadSessionAsync("{\"SchemaVersion\":7}"));
        Assert.ThrowsAsync<InvalidOperationException>(async () => await Codec().LoadSessionAsync("""
            {"SchemaVersion":6,"Cards":[{"Name":"bad","Categories":[],"Copies":1,"DrawCount":1,"Search":{"Selector":{"Kind":"Any","Categories":[]}}}]}
            """));
    }
}
