using System.Text.Json;
using Microsoft.JSInterop;
using Moq;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;
using YGOProbabilityCalculatorBlazor.Services.Session;

namespace YGOProbabilityCalculatorBlazorTest.Services.CardEffects;

public sealed class ArchetypeHandPresetTest {
    private static readonly ICardEffectDefinition[] Definitions = [
        new KetuDracotailEffect(),
        new MitsurugiPrayersEffect(),
        new SpentaTheMagistusSealerEffect(),
        new PrimiteLordlyLodeEffect(),
        new WantedSeekerOfSinfulSpoilsEffect(),
        new SpellPowerMasteryEffect()
    ];

    private static readonly (int Id, string Name)[] Identities = [
        (6153210, "Ketu Dracotail"),
        (45171524, "Mitsurugi Prayers"),
        (42544773, "Spenta, the Magistus Sealer"),
        (56506740, "Primite Lordly Lode"),
        (80845034, "WANTED: Seeker of Sinful Spoils"),
        (38943357, "Spell Power Mastery")
    ];

    [Test]
    public void DefinitionsUseVerifiedCatalogIdentitiesAndOncePerEntryDeckSearchPlans() {
        CardEffectRegistry registry = CreateRegistry();

        Assert.That(registry.All.Select(effect => (effect.CanonicalCardId, effect.Name)), Is.EqualTo(Identities));
        Assert.That(Definitions.SelectMany(definition => definition.Aliases), Is.Empty,
            "The catalog lists one canonical passcode/artwork identity for each of these cards.");

        foreach (ICardEffectDefinition definition in Definitions) {
            DrawEffectPlan plan = definition.Compile();

            Assert.Multiple(() => {
                Assert.That(plan.DrawCount, Is.Null, definition.Name);
                Assert.That(plan.OncePerTurn, Is.True, definition.Name);
                Assert.That(plan.HandCost, Is.Zero, definition.Name);
                Assert.That(plan.RemovalCount, Is.Zero, definition.Name);
                Assert.That(plan.Search, Is.Not.Null, definition.Name);
                Assert.That(plan.Search!.ObjectiveMetadataOnly, Is.True, definition.Name);
            });
        }
    }

    [Test]
    public async Task ImportedSelectedRestoredSavedAndWorkerPlansRetainTheirSearchSelectors() {
        CardEffectRegistry registry = CreateRegistry();
        SessionService codec = new(Mock.Of<IJSRuntime>(), new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());

        foreach ((int id, string name) in Identities) {
            CardInfo info = new() { Id = id, Name = name, CanonicalCardId = id, ArtworkImageIds = [id] };
            Card imported = registry.CreateImportedCard(info, 1, id);
            Card selected = registry.ApplyForExplicitSelection(imported.WithName("Edited display name"), imported, info);
            Card restored = registry.RestoreBuiltIn(imported.WithSearch(null));

            Assert.Multiple(() => {
                Assert.That(imported.EffectProfile.Source, Is.EqualTo(EffectSource.BuiltIn), name);
                Assert.That(selected.EffectPlan, Is.EqualTo(imported.EffectPlan), name);
                Assert.That(restored.EffectPlan, Is.EqualTo(imported.EffectPlan), name);
                Assert.That(restored.EffectProfile.Revision, Is.EqualTo(1), name);
            });

            string sessionJson = codec.SerializeSession(new() { Cards = [imported] });
            Card loaded = (await codec.LoadSessionAsync(sessionJson)).Cards.Single();
            CalculationInput worker = JsonSerializer.Deserialize<CalculationInput>(
                CalculationSnapshot.Capture([loaded], [], 1, []).Json)!;

            Assert.Multiple(() => {
                Assert.That(loaded.EffectPlan, Is.EqualTo(imported.EffectPlan), name);
                Assert.That(worker.Cards.Single().Search, Is.EqualTo(imported.Search), name);
                Assert.That(worker.Cards.Single().OncePerTurn, Is.True, name);
            });
        }
    }

    [Test]
    public void ArchetypePredicatesRequireObjectiveMetadataAndHonorCanonicalIdentityExclusions() {
        CardEffectRegistry registry = CreateRegistry();
        SearchEffectPlan Search(int id) => registry.Find(id)!.Plan.Search!;

        AssertMatches(Search(6153210), Metadata(75003700, "kind:monster", "archetype:dracotail"), true);
        AssertMatches(Search(6153210), Metadata(75003700, "kind:spell", "archetype:dracotail"), false);
        AssertMatches(Search(6153210), Metadata(75003700, "kind:monster", "archetype:dragonmaid"), false);

        AssertMatches(Search(45171524), Metadata(10924999, "kind:monster", "archetype:mitsurugi"), true);
        AssertMatches(Search(45171524), Metadata(10924999, "kind:spell", "archetype:mitsurugi"), false);
        AssertMatches(Search(45171524), Metadata(10924999, "kind:monster", "archetype:reptile"), false);

        AssertMatches(Search(42544773), Metadata(9482987, "kind:monster", "archetype:magistus"), true);
        Card spentaCopy = Metadata(42544773, "kind:monster", "archetype:magistus", CardNameKey("Spenta, the Magistus Sealer"));
        AssertMatches(Search(42544773), spentaCopy, false,
            "A second copy of Spenta cannot be selected.");
        CategoryBase spentaName = spentaCopy.Categories.Single(category => category.MetadataKey == CardNameKey("Spenta, the Magistus Sealer"));
        AssertMatches(Search(42544773), spentaCopy.WithManualMetadataCategory(spentaName), false,
            "A manual metadata toggle cannot change printed identity.");
        AssertMatches(Search(42544773), Metadata(99999999, ["kind:monster", "archetype:magistus"], "Spenta, the Magistus Sealer"), true,
            "A mutable display name does not create printed identity.");
        AssertMatches(Search(42544773), Metadata(9482987, "kind:spell", "archetype:magistus"), false);

        AssertMatches(Search(56506740), Metadata(63198739, "kind:monster", "archetype:primite"), true);
        AssertMatches(Search(56506740), Metadata(99999998, "kind:spell", "archetype:primite"), true,
            "Lordly Lode can search Primite cards of any card type.");
        Card lordlyLodeCopy = Metadata(56506740, "kind:spell", "archetype:primite", CardNameKey("Primite Lordly Lode"));
        AssertMatches(Search(56506740), lordlyLodeCopy, false,
            "A second copy of Lordly Lode cannot be selected.");
        CategoryBase lordlyLodeName = lordlyLodeCopy.Categories.Single(category => category.MetadataKey == CardNameKey("Primite Lordly Lode"));
        AssertMatches(Search(56506740), lordlyLodeCopy.WithManualMetadataCategory(lordlyLodeName), false,
            "A manual metadata toggle cannot change printed identity.");
        AssertMatches(Search(56506740), Metadata(63198739, "kind:monster", "archetype:dragon"), false);

        AssertMatches(Search(80845034), Metadata(72270339, "kind:monster", "archetype:diabellstar"), true);
        AssertMatches(Search(80845034), Metadata(72270339, "kind:monster", "archetype:diabell"), false);
        AssertMatches(Search(80845034), Metadata(72270339, "kind:spell", "archetype:diabellstar"), false);

        AssertMatches(Search(38943357), Metadata(39910367, "kind:spell", "archetype:endymion"), true);
        AssertMatches(Search(38943357), Metadata(3611830, "kind:monster", "archetype:endymion"), true);
        AssertMatches(Search(38943357), Metadata(39910367, "kind:spell", "archetype:spellcaster"), false);
    }

    [TestCase(63166095, "archetype:sky%20striker", "kind:spell")]
    [TestCase(56506740, "archetype:primite", "kind:spell")]
    [TestCase(42544773, "archetype:magistus", "kind:monster")]
    public void SelfExclusionUsesCanonicalIdentityWithoutResolvedNames(int id, string archetype, string kind) {
        ICardEffectDefinition[] definitions = [
            new SkyStrikerMobilizeEngageEffect(),
            new PrimiteLordlyLodeEffect(),
            new SpentaTheMagistusSealerEffect()
        ];
        ICardEffectDefinition definition = definitions.Single(effect => effect.CanonicalCardId == id);
        SearchEffectPlan search = definition.Compile().Search!;
        Card sameIdentity = Metadata(id, archetype, kind).WithName("Edited name");
        Card differentIdentity = Metadata(99999999, archetype, kind, CardNameKey(definition.Name));

        Assert.Multiple(() => {
            AssertMatches(search, sameIdentity, false, "Canonical identity excludes the source without resolved-name metadata.");
            AssertMatches(search, differentIdentity, true, "Shared resolved names do not exclude a different canonical identity.");
        });
    }

    [Test]
    public void HandSelectorsRejectMissingIdentityManualMetadataAndUserCategoryProxies() {
        CardEffectRegistry registry = CreateRegistry();
        SearchEffectPlan ketu = registry.Find(6153210)!.Plan.Search!;
        SearchEffectPlan spenta = registry.Find(42544773)!.Plan.Search!;
        SearchEffectPlan primite = registry.Find(56506740)!.Plan.Search!;

        Card dracotail = Metadata(75003700, "kind:monster", "archetype:dracotail");
        CategoryBase dracotailProperty = dracotail.Categories.Single(category => category.MetadataKey == "archetype:dracotail");
        Card manuallyOverridden = Metadata(75003700, "kind:monster").WithManualMetadataCategory(dracotailProperty);
        Card userProxy = new([new("Dracotail"), new("Monster")], externalCardId: 75003700);
        Card withoutCatalogIdentity = new(dracotail.Categories, name: "Dracotail", externalCardId: null);

        Assert.Multiple(() => {
            Assert.That(ketu.Matches(manuallyOverridden), Is.False);
            Assert.That(ketu.Matches(userProxy), Is.False);
            Assert.That(ketu.Matches(withoutCatalogIdentity), Is.False);
            Assert.That(spenta.Matches(new([new("Magistus", CategorySource.Metadata, "archetype:magistus")], externalCardId: 9482987)), Is.False);
            Assert.That(primite.Matches(new Card([], externalCardId: 63198739).WithManualMetadataCategory(new("Primite", CategorySource.Metadata, "archetype:primite"))), Is.False);
        });
    }

    private static CardEffectRegistry CreateRegistry() => new(Definitions);

    private static Card Metadata(int id, params string[] keys) => Metadata(id, keys, null);

    private static Card Metadata(int id, string[] keys, string? name) => new(
        keys.Append("catalog-id:" + id).Select(key => new CategoryBase(key, CategorySource.Metadata, key)),
        name: name,
        externalCardId: id);

    private static string CardNameKey(string name) => "card-name:" + Uri.EscapeDataString(name.ToLowerInvariant());

    private static void AssertMatches(SearchEffectPlan search, Card candidate, bool expected, string? message = null) =>
        Assert.That(search.Matches(candidate), Is.EqualTo(expected), message);
}
