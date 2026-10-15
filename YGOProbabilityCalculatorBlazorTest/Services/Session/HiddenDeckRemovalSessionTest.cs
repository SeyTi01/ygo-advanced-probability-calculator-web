using System.Text.Json;
using Microsoft.JSInterop;
using Moq;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.DeckImport;
using YGOProbabilityCalculatorBlazor.Services.Session;

namespace YGOProbabilityCalculatorBlazorTest.Services.Session;

public class HiddenDeckRemovalSessionTest {
    private static SessionService Codec() => new(Mock.Of<IJSRuntime>(), new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());

    [Test]
    public async Task PresetSaveShareAndWorkerPreserveHiddenCost() {
        CardInfo info = new() { Id = 35261759, Name = "Pot of Desires" };
        Card pot = CuratedDrawEffectPresets.CreateImportedCard(info, 3, info.Id);
        Assert.That(pot.PreDrawDeckRemovalCount, Is.EqualTo(10));
        Assert.That(pot.DrawCount, Is.EqualTo(2));
        Assert.That(pot.DrawOncePerTurn, Is.True);
        SessionState session = new() { Cards = [pot, new([], 37, id: "filler")], Combos = [new([])], HandSize = 5 };
        string json = Codec().SerializeSession(session);
        string fragment = new Uri(SessionShareCodec.CreateLink("https://example.invalid/", json)).Fragment;
        SessionState loaded = await Codec().LoadSessionAsync(SessionShareCodec.Decode(fragment));
        Assert.That(loaded.SchemaVersion, Is.EqualTo(SessionState.CurrentSchemaVersion));
        Assert.That(loaded.Cards[0].PreDrawDeckRemovalCount, Is.EqualTo(10));
        Assert.That(Codec().SerializeSession(loaded), Is.EqualTo(json));
        CalculationSnapshot snapshot = CalculationSnapshot.Capture(loaded.Cards, loaded.Combos, 5, [], options: new());
        Assert.That(JsonSerializer.Deserialize<CalculationInput>(snapshot.Json)!.Cards[0].PreDrawDeckRemovalCount, Is.EqualTo(10));
        Assert.That(CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json)).TotalProbability, Is.EqualTo(1));
        Card custom = pot.WithPreDrawDeckRemovalCount(3);
        Assert.That(CuratedDrawEffectPresets.ApplyForExplicitSelection(custom, custom, info).PreDrawDeckRemovalCount, Is.EqualTo(3));
        Assert.That(CuratedDrawEffectPresets.ApplyForExplicitSelection(custom, custom, new() { Id = 70368879 }).PreDrawDeckRemovalCount, Is.Zero);
        Assert.That(CuratedDrawEffectPresets.ApplyForExplicitSelection(new([], id: "x"), new([], id: "x"), info).PreDrawDeckRemovalCount, Is.EqualTo(10));
    }

    [TestCase(0), TestCase(1), TestCase(2), TestCase(3), TestCase(4), TestCase(5)]
    public async Task AbsentFieldDefaultsToZeroAndReleasedExtensionsStayIgnored(int schema) {
        string json = $$"""{"SchemaVersion":{{schema}},"Cards":[{"Categories":[],"Copies":1,"Name":"Pot of Desires","ExternalCardId":35261759,"DrawCount":2}]}""";
        Assert.That((await Codec().LoadSessionAsync(json)).Cards.Single().PreDrawDeckRemovalCount, Is.Zero);
        if (schema < 5) {
            json = json.Replace("\"DrawCount\":2", "\"DrawCount\":2,\"PreDrawDeckRemovalCount\":10");
            Assert.That((await Codec().LoadSessionAsync(json)).Cards.Single().PreDrawDeckRemovalCount, Is.Zero);
        }
    }

    [Test]
    public void EveryImmutableReplacementAndCategoryProjectionPreservesCost() {
        CategoryBase dark = new("DARK", CategorySource.Metadata, "attribute:dark");
        CategoryBase role = new("Role", rule: CardSelector.For(dark));
        Card card = new([], drawCount: 2, preDrawDeckRemovalCount: 10);
        Card manual = card.WithManualMetadataCategory(dark);
        Card[] copies = [card.WithName("Changed"), card.WithCopies(2), card.WithActive(false), card.WithCategories([dark]),
            manual, manual.WithoutManualMetadataCategory(dark.MetadataKey!), manual.WithObjectiveMetadata([dark], 123),
            card.WithDrawCount(3), card.WithDrawOncePerTurn(false), card.WithPostDrawDiscardCount(1),
            card.WithPostDrawSelector(CardSelector.Any), card.WithPostDrawFailureBehavior(PostDrawFailureBehavior.FailBranch),
            card.WithPreDrawRemovalCount(1), card.WithPreDrawSelector(CardSelector.Any), card.Duplicate(),
            CategoryMembership.Materialize([manual], [role])[0]];
        foreach (Card copy in copies) {
            Assert.That(copy.PreDrawDeckRemovalCount, Is.EqualTo(10));
        }
        Assert.That(copies[^1].Categories, Does.Contain(role));
        Assert.That(card.Duplicate().Id, Is.Not.EqualTo(card.Id));
        Assert.That(card.WithDrawCount(null).PreDrawDeckRemovalCount, Is.Zero);
        Assert.Throws<ArgumentOutOfRangeException>(() => card.WithPreDrawDeckRemovalCount(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => card.WithPreDrawDeckRemovalCount(61));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Card([], preDrawDeckRemovalCount: 1));
        PinnedCalculationContext before = PinnedCalculationContext.Capture(0, 1, [card], [], [], _ => Guid.Empty, _ => Guid.Empty);
        PinnedCalculationContext after = PinnedCalculationContext.Capture(0, 1, [card.WithPreDrawDeckRemovalCount(0)], [], [], _ => Guid.Empty, _ => Guid.Empty);
        Assert.That(before.DeckDefinition, Is.Not.EqualTo(after.DeckDefinition));
    }

    [TestCase("null"), TestCase("-1"), TestCase("61"), TestCase("1.5"), TestCase("\"1\"")]
    [TestCase("1,\"predrawdeckremovalcount\":1")]
    public void InvalidOrDuplicatedDeckCostsFailClosed(string value) {
        string json = "{\"SchemaVersion\":5,\"Cards\":[{\"Categories\":[],\"Copies\":1,\"Name\":null,\"DrawCount\":2,\"PreDrawDeckRemovalCount\":" + value + "}]}";
        Assert.ThrowsAsync<InvalidOperationException>(() => Codec().LoadSessionAsync(json));
    }
}
