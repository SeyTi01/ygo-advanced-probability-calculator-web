using System.Text.Json;
using Microsoft.JSInterop;
using Microsoft.AspNetCore.Components.Forms;
using Moq;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;
using YGOProbabilityCalculatorBlazor.Services.DeckImport;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.Session;
using YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.CardEffects;

public class CardEffectRegistryTest {
    private static readonly CardEffectRegistry Registry = CardEffectRegistry.Default;
    private static SessionService Codec() => new(Mock.Of<IJSRuntime>(), new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());

    private sealed class AdditionalEffect(int draw = 1, int revision = 1, IReadOnlyList<CategoryBase>? properties = null) : ICardEffectDefinition {
        public IReadOnlyList<CategoryBase> FilterProperties => properties ?? [];
        public int CanonicalCardId => 900001;
        public IReadOnlyList<int> Aliases => [900002];
        public string Name => "Test effect";
        public int Revision => revision;
        public int CompileCalls { get; private set; }
        public DrawEffectPlan Compile() {
            CompileCalls++;

            return DrawEffectPlan.Draw(draw, false) with { HandCost = 1 };
        }
        public EffectSupport Support => EffectSupport.ModeledWithinRules;
        public string Summary => $"Pay one; draw {draw}";
        public string Limitations => "Test-only definition";
    }

    [Test]
    public void AdditionalDefinitionDeterminesImportSelectionWorkerAndExactComputation() {
        AdditionalEffect definition = new();
        CardEffectRegistry registry = new([definition]);
        CardInfo alias = new() { Id = 900002, Name = "Arbitrary name" };
        Card imported = registry.CreateImportedCard(alias, 1, alias.Id);
        Card selected = registry.ApplyForExplicitSelection(new([], id: "row"), new([], id: "row"), alias);
        Assert.That(registry.Find(alias), Is.SameAs(registry.Find(900001)));
        Assert.That(selected.Id, Is.EqualTo("row"));
        Assert.That(selected.EffectPlan, Is.EqualTo(imported.EffectPlan));
        Assert.That(registry.Describe(imported).Summary, Is.EqualTo("Pay one; draw 1"));
        Assert.That(imported.EffectPlan.RequiresHandResolution, Is.True);
        Assert.That(registry.Describe(imported).Capabilities, Is.EqualTo(EffectCapabilities.Draw | EffectCapabilities.HandCost));
        Assert.That(registry.Describe(imported).CanExecute, Is.True);
        List<Card> cards = [imported, new([], id: "target"), new([], 3, id: "filler")];
        List<Combo> combos = [new([], cards: [new("target", 1, 1)], groupId: "g")];
        VerifyOracleAndWorker(cards, 2, combos);
        Card free = imported.WithPreDrawRemovalCount(0);
        Assert.That(Run([free, .. cards.Skip(1)], combos, 2).TotalProbability,
            Is.Not.EqualTo(Run(cards, combos, 2).TotalProbability));
        Assert.That(definition.CompileCalls, Is.EqualTo(1), "Compile only at registration, never per draw transition.");
    }

    [Test]
    public void RegisteredDefinitionsSupplyDistinctFilterPropertiesWithoutCentralRegistration() {
        CategoryBase added = new("New property", CategorySource.Metadata, "test:new");
        List<CategoryBase> properties = [added, new("Same identity", CategorySource.Metadata, "test:new")];
        CardEffectRegistry registry = new([new AdditionalEffect(properties: properties), new PotOfGreedEffect()]);
        properties.Clear();

        Assert.That(registry.FilterProperties, Is.EqualTo(new[] { added }));
        Assert.That(registry.FilterProperties[0].Name, Is.EqualTo("New property"));
        Assert.That(new CardEffectRegistry([new PotOfGreedEffect()]).FilterProperties, Is.Empty);
        Assert.That(CuratedDrawEffectPresets.FilterProperties, Is.SameAs(Registry.FilterProperties));
        Assert.That(Registry.FilterProperties.Select(property => (property.Name, property.MetadataKey)), Is.EquivalentTo(new[] {
            ("Attribute: DARK", "attribute:dark"), ("Quick-Play Spell", "spell-type:quick-play"),
            ("Archetype: Radiant Typhoon", "archetype:radiant%20typhoon"), ("Monster", "kind:monster"),
            ("Level 8", "level:8"), ("Archetype: Orcust", "archetype:orcust"),
            ("Archetype: World Legacy", "archetype:world%20legacy")
        }));
    }

    // Frozen scalar expectations from dev 2a3c7d9, independently of registry output.
    [TestCase(38120068, 2, false, 1, 0, 0)]
    [TestCase(26845680, 2, true, 1, 0, 0)]
    [TestCase(55144522, 2, false, 0, 0, 0)]
    [TestCase(20508881, 2, true, 0, 1, 0)]
    [TestCase(35261759, 2, true, 0, 0, 10)]
    [TestCase(70368879, 1, false, 0, 0, 0)]
    [TestCase(1475311, 2, false, 0, 1, 0)]
    [TestCase(49238328, 2, true, 0, 0, 0)]
    [TestCase(93946239, 1, false, 0, 0, 0)]
    [TestCase(67616300, 1, false, 0, 0, 0)]
    [TestCase(74117290, 1, false, 0, 1, 0)]
    [TestCase(56673480, 1, true, 0, 0, 0)]
    [TestCase(33782437, 1, false, 0, 0, 0)]
    [TestCase(79571449, 3, false, 0, 2, 0)]
    [TestCase(24940422, 2, true, 0, 0, 0)]
    [TestCase(7925734, 1, true, 0, 0, 0)]
    public async Task AllProfilesPreserveFrozenSemanticsAcrossImportSelectionAndPersistence(int id, int draw, bool opt, int pre, int post, int hidden) {
        CardInfo info = new() { Id = id, Name = "Display name" };
        Card card = Registry.CreateImportedCard(info, 2, id);
        Card selected = Registry.ApplyForExplicitSelection(new([]), new([]), info);
        Assert.That((card.DrawCount, card.DrawOncePerTurn, card.PreDrawRemovalCount, card.PostDrawDiscardCount, card.PreDrawDeckRemovalCount),
            Is.EqualTo(((int?)draw, opt, pre, post, hidden)));
        Assert.That(selected.EffectPlan, Is.EqualTo(card.EffectPlan));
        Assert.That(Registry.Describe(card).Source, Is.EqualTo(EffectSource.BuiltIn));
        Assert.That(Registry.Describe(card).Summary, Is.Not.Empty);
        foreach (Card variant in new[] { card, card.WithDrawCount(1) }) {
            SessionState session = new() { Cards = [variant] };
            string json = Codec().SerializeSession(session);
            string fragment = new Uri(SessionShareCodec.CreateLink("https://example.invalid/", json)).Fragment;
            Card loaded = (await Codec().LoadSessionAsync(SessionShareCodec.Decode(fragment))).Cards.Single();
            Assert.That(loaded.EffectPlan, Is.EqualTo(variant.EffectPlan));
            Assert.That(loaded.EffectProfile, Is.EqualTo(variant.EffectProfile));
            CalculationInput wire = JsonSerializer.Deserialize<CalculationInput>(CalculationSnapshot.Capture([loaded], [], 1, []).Json)!;
            Assert.That(wire.Cards[0].EffectProfile, Is.EqualTo(variant.EffectProfile));
        }
    }

    [Test]
    public void SameArtworkIdentityPreservesExplicitEditsEvenIfEqualToBuiltIn() {
        CardInfo info = new() { Id = 35261759, Name = "Pot" };
        Card built = Registry.CreateImportedCard(info, 3, info.Id);
        Card custom = built.WithDrawCount(2);
        Assert.That(custom.EffectProfile.Source, Is.EqualTo(EffectSource.Custom));
        CardInfo alias = new() { Id = 99, CanonicalCardId = info.Id, ArtworkImageIds = [info.Id, 99] };
        Card replacement = custom.WithObjectiveMetadata([], 99);
        Assert.That(Registry.ApplyForExplicitSelection(replacement, custom, alias).EffectProfile.Source, Is.EqualTo(EffectSource.Custom));
        Card none = custom.WithDrawCount(null);
        Assert.That(CuratedDrawEffectPresets.ApplyToSelectedCardIfPristine(none, info).DrawCount, Is.Null);
        Assert.That(Registry.ApplyForExplicitSelection(none, none, new() { Id = 70368879 }).DrawCount, Is.EqualTo(1));
        Assert.That(Registry.CreateImportedCard(new() { Id = 999, Name = "Pot of Desires" }, 1, 999).DrawCount, Is.Null);
    }

    [Test]
    public void MetadataProjectionDuplicationAndRestorePreserveUnrelatedState() {
        CategoryBase dark = new("DARK", CategorySource.Metadata, "attribute:dark");
        CategoryBase user = new("Role");
        Card built = Registry.CreateImportedCard(new() { Id = 35261759 }, 3, 35261759);
        Card custom = built.WithDrawCount(null).WithName("Custom name").WithCategories([user])
            .WithManualMetadataCategory(dark).WithActive(false);
        Card projected = CategoryMembership.Materialize([custom], [new("Derived", rule: CardSelector.For(dark))])[0];
        Assert.That(projected.EffectProfile, Is.EqualTo(custom.EffectProfile));
        Card duplicate = projected.Duplicate();
        Assert.That(duplicate.Id, Is.Not.EqualTo(projected.Id));
        Assert.That(duplicate.EffectProfile, Is.EqualTo(projected.EffectProfile));
        Card restored = Registry.RestoreBuiltIn(projected);
        Assert.Multiple(() => {
            Assert.That(restored.EffectPlan, Is.EqualTo(built.EffectPlan));
            Assert.That(restored.EffectProfile.Source, Is.EqualTo(EffectSource.BuiltIn));
            Assert.That(restored.Id, Is.EqualTo(projected.Id));
            Assert.That(restored.Copies, Is.EqualTo(3));
            Assert.That(restored.Name, Is.EqualTo("Custom name"));
            Assert.That(restored.Active, Is.False);
            Assert.That(restored.ExternalCardId, Is.EqualTo(projected.ExternalCardId));
            Assert.That(restored.Categories, Is.EqualTo(projected.Categories));
            Assert.That(restored.ManualMetadataCategoryKeys, Is.EqualTo(projected.ManualMetadataCategoryKeys));
        });
    }

    [Test]
    public async Task ChangedOrRemovedDefinitionCannotRewriteSavedBuiltInOrCustomSnapshots() {
        CardEffectRegistry old = new([new AdditionalEffect()]);
        CardEffectRegistry changed = new([new AdditionalEffect(3, 2)]);
        Card original = old.CreateImportedCard(new() { Id = 900001 }, 1, 900001);
        foreach (Card card in new[] { original, original.WithDrawCount(2) }) {
            string json = Codec().SerializeSession(new() { Cards = [card] });
            Card loaded = (await Codec().LoadSessionAsync(json)).Cards.Single();
            Assert.That(changed.Describe(loaded).Plan, Is.EqualTo(card.EffectPlan));
            Assert.That(changed.Describe(loaded).Source, Is.EqualTo(card.EffectProfile.Source));
            Assert.That(changed.RestoreBuiltIn(loaded).DrawCount, Is.EqualTo(3));
            CardEffectRegistry removed = new([]);
            Assert.That(removed.Describe(loaded).CanExecute, Is.True);
            Assert.That(removed.Describe(loaded).CanRestore, Is.False);
            Assert.Throws<InvalidOperationException>(() => removed.RestoreBuiltIn(loaded));
            VerifyOracleAndWorker([loaded, new([], id: "target"), new([], 3)], 2,
                [new([], cards: [new("target", 1, 1)])]);
        }
    }

    [TestCase(EffectExecutionModel.Unsupported)]
    [TestCase((EffectExecutionModel)99)]
    public async Task UnsupportedCapabilityIsPreservedAndFailsClosedUntilExplicitlyDisabled(EffectExecutionModel model) {
        Card card = Registry.CreateImportedCard(new() { Id = 55144522 }, 1, 55144522);
        card = card.WithEffect(card.EffectPlan, card.EffectProfile with { ExecutionModel = model });
        Card loaded = (await Codec().LoadSessionAsync(Codec().SerializeSession(new() { Cards = [card] }))).Cards.Single();
        Assert.That(loaded.EffectProfile.ExecutionModel, Is.EqualTo(model));
        Assert.That(Registry.Describe(loaded).CanExecute, Is.False);
        List<Card> cards = [loaded, new([], 5)];
        foreach (bool conditions in new[] { false, true }) {
            CalculationSnapshot snapshot = CalculationSnapshot.Capture(cards, [new([])], 1, [], options: new(conditions));
            Assert.Throws<ArgumentException>(() => CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json)));
            Assert.Throws<ArgumentException>(() => new ProbabilityCalculatorService().CalculateProbabilityResults(cards, [new([])], 1));
        }
        Assert.That(Run(cards, [new([])], 1, false).TotalProbability, Is.EqualTo(1));
        Assert.That(Registry.RestoreBuiltIn(loaded).EffectProfile.ExecutionModel, Is.EqualTo(EffectExecutionModel.DrawSequenceV1));
    }

    [Test]
    public async Task YdkAndYdkeImportsCarryMaintainedProvenance() {
        Mock<ICardInfoService> catalog = new();
        catalog.Setup(service => service.GetCardInfoAsync(35261759))
            .ReturnsAsync(new CardInfo { Id = 35261759, Name = "Pot of Desires" });
        Mock<IFileService> files = new();
        files.Setup(service => service.ReadAllLinesAsync(It.IsAny<IBrowserFile>()))
            .ReturnsAsync(["#main", "35261759", "35261759", "#extra"]);
        DeckImportService importer = new(catalog.Object, files.Object);
        byte[] bytes = new byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes, 35261759);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 35261759);
        string ydke = "ydke://" + Convert.ToBase64String(bytes) + "!!!";
        foreach (List<Card> imported in new[] { await importer.ImportDeckFromYdkAsync(Mock.Of<IBrowserFile>()),
            await importer.ImportDeckFromYdkeAsync(ydke) }) {
            Assert.That(imported.Single().Copies, Is.EqualTo(2));
            Assert.That(imported.Single().EffectProfile.Source, Is.EqualTo(EffectSource.BuiltIn));
            Assert.That(imported.Single().PreDrawDeckRemovalCount, Is.EqualTo(10));
        }
    }

    [TestCase("null")]
    [TestCase("{\"Source\":1}")]
    [TestCase("{\"Source\":99}")]
    [TestCase("{\"Revision\":0}")]
    [TestCase("{\"CanonicalCardId\":-1}")]
    public void InvalidProvenanceFailsClosed(string profile) {
        string json = "{\"SchemaVersion\":5,\"Cards\":[{\"Categories\":[],\"Copies\":1,\"Name\":null,\"EffectProfile\":" + profile + "}]}";
        Assert.ThrowsAsync<InvalidOperationException>(() => Codec().LoadSessionAsync(json));
    }

    [TestCase(0), TestCase(3), TestCase(4), TestCase(5)]
    public async Task LegacyFilesNeverInferBuiltInOwnership(int schema) {
        string json = $$"""{"SchemaVersion":{{schema}},"Cards":[{"Categories":[],"Copies":1,"Name":"Pot of Greed","ExternalCardId":55144522,"DrawCount":1}]}""";
        Card card = (await Codec().LoadSessionAsync(json)).Cards.Single();
        Assert.That(card.EffectProfile.Source, Is.EqualTo(EffectSource.LegacySnapshot));
        Assert.That(card.DrawCount, Is.EqualTo(schema < 4 ? (int?)null : 1));
        Assert.That(Registry.Describe(card).CanRestore, Is.True);
    }

    [TestCase(38120068), TestCase(26845680), TestCase(1475311), TestCase(20508881), TestCase(79571449)]
    public void MaintainedConditionalProfilesMatchPhysicalOrderOracle(int id) {
        Card source = Registry.CreateImportedCard(new() { Id = id }, 1, id);
        CategoryBase dark = new("DARK", CategorySource.Metadata, "attribute:dark");
        CategoryBase level = new("Level 8", CategorySource.Metadata, "level:8");
        CategoryBase orcust = new("Orcust", CategorySource.Metadata, "archetype:orcust");
        CategoryBase monster = new("Monster", CategorySource.Metadata, "kind:monster");
        CategoryBase spell = new("Quick-Play", CategorySource.Metadata, "spell-type:quick-play");
        List<Card> deck = [source, new([dark, level, orcust, monster], id: "target"), new([dark, spell]), new([], 3)];
        VerifyOracleAndWorker(deck, 2, [new([], cards: [new("target", 1, 1)], groupId: "g"), new([new(dark, 0, 0)])]);
    }

    [Test]
    public void DesiresAndMixedChainProfilesMatchPhysicalHiddenPositions() {
        Card pot = Registry.CreateImportedCard(new() { Id = 35261759 }, 1, 35261759);
        Card allure = Registry.CreateImportedCard(new() { Id = 1475311 }, 1, 1475311);
        CategoryBase dark = new("DARK", CategorySource.Metadata, "attribute:dark");
        List<Card> deck = [pot, allure, new([dark], id: "target"), new([], 12)];
        VerifyOracleAndWorker(deck, 2, [new([], cards: [new("target", 1, 1)], groupId: "g"), new([new(dark, 0, 0)])]);
    }

    private static ProbabilityCalculationResult Run(List<Card> cards, List<Combo> combos, int hand, bool enabled = true) =>
        CalculationWire.ReadResult(CalculationWire.Execute(CalculationSnapshot.Capture(cards, combos, hand,
            [new("g", "Group"), new("empty", "Empty")], drawEffectsEnabled: enabled, options: new(true)).Json));

    private static void VerifyOracleAndWorker(List<Card> deck, int hand, List<Combo> combos) {
        List<ComboGroup> groups = [new("g", "Group"), new("empty", "Empty")];
        // Include an always-satisfied route so the total independently checks
        // mandatory failure mass, in addition to individual and grouped rows.
        combos = [.. combos, new([])];
        DrawSequenceOracle.Counts oracle = DrawSequenceOracle.Enumerate(deck, hand, combos, groups, true);
        ExactCalculationResult result = new DrawEffectCalculation(deck, hand, new(CalculationWorkPolicy.Interactive), true).Calculate(combos, groups);
        ExactProbability[] fractions = [result.Total, .. result.Combos, .. result.Groups];
        for (int i = 0; i < fractions.Length; i++) {
            Assert.That(fractions[i].Numerator * oracle.Total, Is.EqualTo(oracle.Wins[i] * fractions[i].Denominator));
        }
        ProbabilityCalculationResult native = new ProbabilityCalculatorService().CalculateProbabilityResults(deck, combos, hand,
            groups, CalculationWorkPolicy.Interactive, new(true));
        Assert.That(JsonSerializer.Serialize(Run(deck, combos, hand)), Is.EqualTo(JsonSerializer.Serialize(native)));
    }
}
