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
public class FilteredPostDrawSessionTest {
    private static SessionService Codec() => new(Mock.Of<IJSRuntime>(), new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());

    [TestCase(1475311), TestCase(20508881)]
    public async Task FilteredPostDrawSessionShareAndCalculationWireRoundTrip(int id) {
        Card card = CuratedDrawEffectPresets.CreateImportedCard(new() { Id = id, Name = "Effect" }, 1, id);
        SessionState session = new() { Cards = [card, new([], 3, id: "f")], Combos = [new([])], HandSize = 1 };
        string json = Codec().SerializeSession(session);
        string fragment = new Uri(SessionShareCodec.CreateLink("https://example.invalid/", json)).Fragment;
        SessionState loaded = await Codec().LoadSessionAsync(SessionShareCodec.Decode(fragment));
        Assert.That(loaded.SchemaVersion, Is.EqualTo(5));
        Assert.That(loaded.Cards[0].PostDrawSelector, Is.EqualTo(card.PostDrawSelector));
        Assert.That(loaded.Cards[0].PostDrawFailureBehavior, Is.EqualTo(PostDrawFailureBehavior.FailBranch));
        Assert.That(Codec().SerializeSession(loaded), Is.EqualTo(json));
        CalculationSnapshot snapshot = CalculationSnapshot.Capture(loaded.Cards, loaded.Combos, 1, [], options: new());
        CalculationInput input = JsonSerializer.Deserialize<CalculationInput>(snapshot.Json)!;
        Assert.That(input.Cards[0].PostDrawSelector, Is.EqualTo(card.PostDrawSelector));
        Assert.That(CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json)).TotalProbability, Is.EqualTo(0.75));
        Assert.That(loaded.Cards[0].WithObjectiveMetadata([], id).PostDrawSelector, Is.EqualTo(card.PostDrawSelector));
    }

    [TestCase(1475311), TestCase(20508881)]
    public async Task FilteredPostDrawOldSchemaFourNeverInfersCuratedBehavior(int id) {
        string json = $$"""{"SchemaVersion":4,"Cards":[{"Categories":[],"Copies":1,"Name":"Old","ExternalCardId":{{id}},"DrawCount":2}]}""";
        Card card = (await Codec().LoadSessionAsync(json)).Cards.Single();
        Assert.That(card.PostDrawDiscardCount, Is.Zero);
        Assert.That(card.PostDrawSelector, Is.EqualTo(CardSelector.Any));
        Assert.That(card.WithObjectiveMetadata([], id).PostDrawSelector, Is.EqualTo(CardSelector.Any));
        Assert.That(CuratedDrawEffectPresets.ApplyToSelectedCardIfPristine(card, new() { Id = id }).PostDrawDiscardCount, Is.Zero);
        Card unrestricted = (await Codec().LoadSessionAsync(json.Replace("\"DrawCount\":2", "\"DrawCount\":2,\"PostDrawDiscardCount\":1"))).Cards.Single();
        Assert.That(unrestricted.PostDrawSelector, Is.EqualTo(CardSelector.Any));
    }

    [Test]
    public void FilteredPostDrawAllImmutableReplacementsAndPinnedContextPreserveMeaning() {
        CategoryBase property = new("Property", CategorySource.Metadata, "attribute:dark");
        Card card = new([], id: "stable", drawCount: 2, postDrawDiscardCount: 1, postDrawSelector: CardSelector.For(property));
        Card manual = card.WithManualMetadataCategory(property);
        Card[] replacements = [card.WithName("Changed"), card.WithCopies(3), card.WithActive(false), card.WithCategories([property]),
            manual, manual.WithoutManualMetadataCategory(property.MetadataKey!), manual.WithObjectiveMetadata([property], 123),
            card.WithDrawCount(3), card.WithDrawOncePerTurn(false), card.WithPostDrawDiscardCount(2),
            card.WithPostDrawFailureBehavior(PostDrawFailureBehavior.FailBranch)];
        foreach (Card replacement in replacements) {
            Assert.That(replacement.PostDrawSelector, Is.EqualTo(card.PostDrawSelector));
            Assert.That(replacement.PostDrawFailureBehavior, Is.EqualTo(card.PostDrawFailureBehavior));
            Assert.That(replacement.Id, Is.EqualTo(card.Id));
        }
        Assert.That(card.WithDrawCount(null).PostDrawSelector, Is.EqualTo(CardSelector.Any));
        Assert.That(card.WithPostDrawDiscardCount(0).PostDrawSelector, Is.EqualTo(CardSelector.Any));
        Assert.Throws<ArgumentException>(() => new Card([], postDrawSelector: card.PostDrawSelector));
        Assert.Throws<ArgumentOutOfRangeException>(() => card.WithPostDrawFailureBehavior((PostDrawFailureBehavior)123));
        PinnedCalculationContext filtered = PinnedCalculationContext.Capture(0, 1, [card], [], [], _ => Guid.Empty, _ => Guid.Empty);
        PinnedCalculationContext any = PinnedCalculationContext.Capture(0, 1, [card.WithPostDrawSelector(CardSelector.Any)], [], [], _ => Guid.Empty, _ => Guid.Empty);
        Assert.That(filtered.DeckDefinition, Is.Not.EqualTo(any.DeckDefinition));
    }

    [TestCase("\"PostDrawSelector\":null")]
    [TestCase("\"PostDrawSelector\":{\"Kind\":\"AnyOf\",\"Categories\":[]}")]
    [TestCase("\"PostDrawSelector\":{\"Kind\":\"Any\",\"Categories\":[]},\"postdrawselector\":{}")]
    [TestCase("\"PostDrawFailureBehavior\":\"Ignore\"")]
    [TestCase("\"PostDrawFailureBehavior\":0")]
    [TestCase("\"PostDrawFailureBehavior\":\"FailBranch\",\"postdrawfailurebehavior\":\"FailBranch\"")]
    public void FilteredPostDrawSessionRejectsMalformedRequirements(string field) {
        string json = "{\"SchemaVersion\":4,\"Cards\":[{\"Categories\":[],\"Copies\":1,\"Name\":null,\"DrawCount\":2,\"PostDrawDiscardCount\":1," + field + "}]}";
        Assert.ThrowsAsync<InvalidOperationException>(() => Codec().LoadSessionAsync(json));
    }
}
