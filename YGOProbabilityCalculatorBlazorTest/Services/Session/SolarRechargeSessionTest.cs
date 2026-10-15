using System.Text.Json;
using Microsoft.JSInterop;
using Moq;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;
using YGOProbabilityCalculatorBlazor.Services.DeckImport;
using YGOProbabilityCalculatorBlazor.Services.Session;

namespace YGOProbabilityCalculatorBlazorTest.Services.Session;

public class SolarRechargeSessionTest {
    private static SessionService Codec() => new(Mock.Of<IJSRuntime>(), new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());
    private static Card Solar() => CardEffectRegistry.Default.CreateImportedCard(new() { Id = 691925, Name = "Localized name" }, 3, 691925);

    [Test]
    public async Task SavedRevisionShareAndWorkerAreIndependentOfCurrentDefinition() {
        Card card = Solar();
        Card saved = card.WithEffect(card.EffectPlan with { HiddenPostDrawMill = 1 }, card.EffectProfile with { Revision = 17 });
        SessionState session = new() { Cards = [saved, new([], 37, id: "filler")], Combos = [new([])], HandSize = 5 };
        string json = Codec().SerializeSession(session);
        string fragment = new Uri(SessionShareCodec.CreateLink("https://example.invalid/", json)).Fragment;
        SessionState loaded = await Codec().LoadSessionAsync(SessionShareCodec.Decode(fragment));
        Assert.That(loaded.SchemaVersion, Is.EqualTo(SessionState.CurrentSchemaVersion));
        Assert.That(loaded.Cards[0].EffectPlan, Is.EqualTo(saved.EffectPlan));
        Assert.That(loaded.Cards[0].EffectProfile, Is.EqualTo(saved.EffectProfile));
        Assert.That(Codec().SerializeSession(loaded), Is.EqualTo(json));
        CalculationSnapshot snapshot = CalculationSnapshot.Capture(loaded.Cards, loaded.Combos, 5, [], options: new());
        WorkerCard wire = JsonSerializer.Deserialize<CalculationInput>(snapshot.Json)!.Cards[0];
        Assert.That(wire.PostDrawDeckMillCount, Is.EqualTo(1));
        Assert.That(wire.EffectProfile, Is.EqualTo(saved.EffectProfile));
        Assert.That(CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json)).TotalProbability, Is.EqualTo(1));
        Assert.That(CardEffectRegistry.Default.RestoreBuiltIn(loaded.Cards[0]).EffectPlan, Is.EqualTo(card.EffectPlan));
    }

    [Test]
    public void CanonicalSelectionAliasesAndAllImmutableUpdatesPreservePlan() {
        Card card = Solar();
        CategoryBase monster = new("Monster", CategorySource.Metadata, "kind:monster");
        CategoryBase role = new("Role", rule: CardSelector.For(monster));
        Card manual = card.WithManualMetadataCategory(monster);
        Card[] updates = [card.WithName("Renamed"), card.WithCopies(2), card.WithActive(false), card.WithCategories([monster]),
            manual, manual.WithoutManualMetadataCategory(monster.MetadataKey!), manual.WithObjectiveMetadata([monster], 691925),
            card.WithDrawCount(3), card.WithDrawOncePerTurn(true), card.WithPostDrawDiscardCount(1),
            card.WithPostDrawSelector(CardSelector.Any), card.WithPostDrawFailureBehavior(PostDrawFailureBehavior.FailBranch),
            card.WithPreDrawRemovalCount(0), card.WithPreDrawSelector(CardSelector.For(monster)), card.WithPreDrawDeckRemovalCount(1),
            card.Duplicate(), CategoryMembership.Materialize([manual], [role])[0]];
        Assert.That(updates.All(update => update.PostDrawDeckMillCount == 2), Is.True);
        Assert.That(card.Duplicate().Id, Is.Not.EqualTo(card.Id));
        Assert.That(card.WithDrawCount(null).PostDrawDeckMillCount, Is.Zero);
        Assert.Throws<ArgumentOutOfRangeException>(() => new Card([], postDrawDeckMillCount: 1));
        Card custom = card.WithPostDrawDeckMillCount(1);
        CardInfo alias = new() { Id = 123456, CanonicalCardId = 691925, ArtworkImageIds = [691925, 123456] };
        CardEffectRegistry registry = CardEffectRegistry.Default;
        Assert.That(registry.CreateImportedCard(alias, 1, alias.Id).EffectPlan, Is.EqualTo(card.EffectPlan));
        Assert.That(registry.ApplyForExplicitSelection(custom, custom, alias), Is.SameAs(custom));
        Assert.That(registry.ApplyForExplicitSelection(custom, custom, new() { Id = 70368879 }).PostDrawDeckMillCount, Is.Zero);
        Assert.That(registry.RestoreBuiltIn(custom).EffectPlan, Is.EqualTo(card.EffectPlan));
        Assert.That(registry.Describe(card).Capabilities.HasFlag(EffectCapabilities.HiddenPostDrawMill), Is.True);
        PinnedCalculationContext before = PinnedCalculationContext.Capture(0, 1, [card], [], [], _ => Guid.Empty, _ => Guid.Empty);
        PinnedCalculationContext after = PinnedCalculationContext.Capture(0, 1, [custom], [], [], _ => Guid.Empty, _ => Guid.Empty);
        Assert.That(before.DeckDefinition, Is.Not.EqualTo(after.DeckDefinition));
    }

    [TestCase(0), TestCase(1), TestCase(2), TestCase(3), TestCase(4), TestCase(5)]
    public async Task OldPlansKeepTheirMeaningWithoutEnrichment(int schema) {
        string json = $$"""{"SchemaVersion":{{schema}},"Cards":[{"Categories":[],"Copies":1,"Name":"Solar Recharge","ExternalCardId":691925,"DrawCount":2}]}""";
        Assert.That((await Codec().LoadSessionAsync(json)).Cards.Single().PostDrawDeckMillCount, Is.Zero);
        if (schema < 5) {
            Assert.That((await Codec().LoadSessionAsync(json.Replace("\"DrawCount\":2", "\"DrawCount\":2,\"PostDrawDeckMillCount\":2")))
                .Cards.Single().PostDrawDeckMillCount, Is.Zero);
        }
    }

    [TestCase("null"), TestCase("-1"), TestCase("61"), TestCase("1.5"), TestCase("\"1\"")]
    [TestCase("1,\"postdrawdeckmillcount\":1")]
    public void InvalidMillFieldsFailClosed(string value) {
        string json = "{\"SchemaVersion\":5,\"Cards\":[{\"Categories\":[],\"Copies\":1,\"Name\":null,\"DrawCount\":2,\"PostDrawDeckMillCount\":" + value + "}]}";
        Assert.ThrowsAsync<InvalidOperationException>(() => Codec().LoadSessionAsync(json));
    }
}
