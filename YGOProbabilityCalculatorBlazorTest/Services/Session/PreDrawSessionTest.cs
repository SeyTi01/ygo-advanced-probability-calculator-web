using System.Text.Json;
using Microsoft.JSInterop;
using Moq;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.DeckImport;
using YGOProbabilityCalculatorBlazor.Services.Session;

namespace YGOProbabilityCalculatorBlazorTest.Services.Session;

[TestFixture]
public class PreDrawSessionTest {
    private static SessionService Codec() => new(Mock.Of<IJSRuntime>(), new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());

    [TestCase(38120068), TestCase(26845680)]
    public async Task PreDrawSchemaFiveSaveShareRecoveryAndWorkerPreserveCompositeSelectors(int id) {
        Card card = CuratedDrawEffectPresets.CreateImportedCard(new() { Id = id, Name = "Effect" }, 1, id);
        SessionState session = new() { Cards = [card, new([], 3, id: "f")], Combos = [new([])], HandSize = 1 };
        string json = Codec().SerializeSession(session);
        string fragment = new Uri(SessionShareCodec.CreateLink("https://example.invalid/", json)).Fragment;
        SessionState loaded = await Codec().LoadSessionAsync(SessionShareCodec.Decode(fragment));
        Assert.That(loaded.SchemaVersion, Is.EqualTo(5));
        Assert.That(loaded.Cards[0].PreDrawRemovalCount, Is.EqualTo(1));
        Assert.That(loaded.Cards[0].PreDrawSelector, Is.EqualTo(card.PreDrawSelector));
        Assert.That(Codec().SerializeSession(loaded), Is.EqualTo(json));
        CalculationSnapshot snapshot = CalculationSnapshot.Capture(loaded.Cards, loaded.Combos, 1, [], options: new());
        CalculationInput input = JsonSerializer.Deserialize<CalculationInput>(snapshot.Json)!;
        Assert.That(input.Cards[0].PreDrawSelector, Is.EqualTo(card.PreDrawSelector));
        Assert.That(input.Cards[0].PreDrawRemovalCount, Is.EqualTo(1));
        Assert.That(CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json)).TotalProbability, Is.EqualTo(1));
        Assert.That(card.DrawOncePerTurn, Is.EqualTo(id == 26845680));
        Assert.That(card.DrawCount, Is.EqualTo(2));
    }

    [TestCase(0), TestCase(1), TestCase(2), TestCase(3), TestCase(4)]
    public async Task PreDrawOlderSchemasNeverInferOrActivateCosts(int schema) {
        foreach (int id in new[] { 38120068, 26845680 }) {
            string json = $$$"""{"SchemaVersion":{{{schema}}},"Cards":[{"Categories":[],"Copies":1,"Name":"Old","ExternalCardId":{{{id}}},"DrawCount":2,"PreDrawRemovalCount":1,"PreDrawSelector":{"Kind":"Any"}}]}""";
            SessionState loaded = await Codec().LoadSessionAsync(json);
            Card card = loaded.Cards.Single();
            Assert.That(loaded.SchemaVersion, Is.EqualTo(5));
            Assert.That(card.PreDrawRemovalCount, Is.Zero);
            Assert.That(card.PreDrawSelector, Is.EqualTo(CardSelector.Any));
            Assert.That(card.DrawCount, Is.EqualTo(schema == 4 ? 2 : (int?)null));
            Assert.That(card.WithObjectiveMetadata([], id).PreDrawRemovalCount, Is.Zero);
        }
    }

    [Test]
    public void PreDrawImmutableCopiesAndPinnedContextPreserveConfiguration() {
        CategoryBase property = new("DARK", CategorySource.Metadata, "attribute:dark");
        Card card = new([], id: "stable", drawCount: 2, preDrawRemovalCount: 1, preDrawSelector: CardSelector.For(property));
        Card manual = card.WithManualMetadataCategory(property);
        Card[] replacements = [card.WithName("Changed"), card.WithCopies(3), card.WithActive(false), card.WithCategories([property]),
            manual, manual.WithoutManualMetadataCategory(property.MetadataKey!), manual.WithObjectiveMetadata([property], 123),
            card.WithDrawCount(3), card.WithDrawOncePerTurn(false), card.WithPostDrawDiscardCount(2),
            card.WithPostDrawSelector(CardSelector.Any), card.WithPostDrawFailureBehavior(PostDrawFailureBehavior.FailBranch)];
        foreach (Card replacement in replacements) {
            Assert.That(replacement.PreDrawRemovalCount, Is.EqualTo(1));
            Assert.That(replacement.PreDrawSelector, Is.EqualTo(card.PreDrawSelector));
            Assert.That(replacement.Id, Is.EqualTo(card.Id));
        }
        Assert.That(card.WithDrawCount(null).PreDrawRemovalCount, Is.Zero);
        Assert.That(card.WithDrawCount(null).PreDrawSelector, Is.EqualTo(CardSelector.Any));
        Assert.That(card.WithPreDrawRemovalCount(0).PreDrawSelector, Is.EqualTo(CardSelector.Any));
        Assert.Throws<ArgumentException>(() => new Card([], drawCount: 2, preDrawSelector: card.PreDrawSelector));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Card([], preDrawRemovalCount: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => card.WithPreDrawRemovalCount(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => card.WithPreDrawRemovalCount(-1));
        PinnedCalculationContext filtered = PinnedCalculationContext.Capture(0, 1, [card], [], [], _ => Guid.Empty, _ => Guid.Empty);
        PinnedCalculationContext any = PinnedCalculationContext.Capture(0, 1, [card.WithPreDrawSelector(CardSelector.Any)], [], [], _ => Guid.Empty, _ => Guid.Empty);
        Assert.That(filtered.DeckDefinition, Is.Not.EqualTo(any.DeckDefinition));
    }

    [TestCase("\"PreDrawRemovalCount\":null")]
    [TestCase("\"PreDrawRemovalCount\":-1")]
    [TestCase("\"PreDrawRemovalCount\":3")]
    [TestCase("\"PreDrawRemovalCount\":1.5")]
    [TestCase("\"PreDrawRemovalCount\":\"1\"")]
    [TestCase("\"PreDrawRemovalCount\":1,\"predrawremovalcount\":1")]
    [TestCase("\"PreDrawRemovalCount\":1,\"PreDrawSelector\":null")]
    [TestCase("\"PreDrawRemovalCount\":1,\"PreDrawSelector\":{\"Kind\":\"AnyOf\",\"Categories\":[]}")]
    [TestCase("\"PreDrawRemovalCount\":1,\"PreDrawSelector\":{\"Kind\":\"Any\"},\"predrawselector\":{}")]
    [TestCase("\"PreDrawSelector\":{\"Kind\":\"Category\",\"Categories\":[\"user:A\"]}")]
    public void PreDrawMalformedOrDuplicateFieldsFailClosed(string fields) {
        string json = "{\"SchemaVersion\":5,\"Cards\":[{\"Categories\":[],\"Copies\":1,\"Name\":null,\"DrawCount\":2," + fields + "}]}";
        Assert.ThrowsAsync<InvalidOperationException>(() => Codec().LoadSessionAsync(json));
    }
}
