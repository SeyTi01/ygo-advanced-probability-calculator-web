using System.Text.Json;
using Bunit;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.DeckImport;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.Session;
using YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.Session;

[TestFixture]
public class CatalogArchetypeReconcilerTest {
    private static readonly CategoryBase Lightsworn = new("Archetype: Lightsworn", CategorySource.Metadata, "archetype:lightsworn");
    private static readonly CategoryBase Fire = new("Attribute: FIRE", CategorySource.Metadata, "attribute:fire");

    public static IReadOnlyList<CardInfo> BundledCatalog() {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null) {
            string path = Path.Combine(directory.FullName, "YGOProbabilityCalculatorBlazor", "wwwroot", "data", "card-catalog.v1.json");
            if (File.Exists(path)) {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
                Assert.That(document.RootElement.GetProperty("version").GetInt32(), Is.EqualTo(2));
                return document.RootElement.GetProperty("cards").Deserialize<CardInfo[]>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            }
            directory = directory.Parent;
        }
        throw new FileNotFoundException("Bundled catalog not found.");
    }

    [Test]
    public void CatalogResolvesRepresentativeNamesClausesAndEffectSelectors() {
        IReadOnlyList<CardInfo> catalog = BundledCatalog();
        foreach ((string name, string[] expected) in new (string, string[])[] {
            ("Solar Recharge", []), ("Charge of the Light Brigade", []),
            ("Summoned Skull", ["Archfiend"]), ("Hardened Armed Dragon", []),
            ("A Legendary Ocean", ["Umi"]), ("Fusion Substitute", ["Polymerization"]),
            ("Lemuria, the Forgotten City", ["Umi"]),
            ("Neo-Spacian Marine Dolphin", ["Neo-Spacian"]), ("Neo-Spacian Twinkle Moss", ["Neo-Spacian"]),
            ("Double Iris Magician", ["Magician", "Pendulum", "Pendulum Dragon"]),
            ("Orcustrated Return", ["Orcust"]), ("World Legacy - \"World Wand\"", ["World Legacy"]),
            ("Radiant Typhoon Vision", ["Radiant Typhoon"])
        }) {
            Assert.That(catalog.Single(card => card.Name == name).ResolvedArchetypes, Is.EquivalentTo(expected), name);
        }
        CardInfo raiden = catalog.Single(card => card.Name == "Raiden, Hand of the Lightsworn");
        Assert.That(CardPropertyProvider.GetCategories(raiden).Select(category => category.MetadataKey),
            Does.Contain("archetype:lightsworn").And.Contain("kind:monster"));
    }

    [Test]
    public async Task ReviewedNameTreatmentPreservesImportedAndRecoveredIdentityAndSavedSimulation() {
        IReadOnlyList<CardInfo> catalog = BundledCatalog();
        CategoryBase manual = new("Archetype: Lightsworn", CategorySource.Metadata, "archetype:lightsworn");
        foreach (int externalId in new[] { 34103656, 13857930, 78734254 }) {
            CardInfo info = catalog.Single(card => card.Id == externalId);
            Assert.That(info.CanonicalCardId, Is.EqualTo(externalId));
            Card imported = CuratedDrawEffectPresets.CreateImportedCard(info, 2, info.Id);
            Assert.That((imported.Name, imported.ExternalCardId, imported.Copies, imported.EffectProfile.CanonicalCardId),
                Is.EqualTo((info.Name, (int?)externalId, 2, (int?)externalId)));
            Assert.That(imported.Categories.All(category => category.MetadataKey?.Contains("treated-as", StringComparison.Ordinal) != true), Is.True);
            Card savedCard = new([manual, Fire, new("Manual role")], 2, imported.Name, active: false,
                id: imported.Id, externalCardId: externalId, manualMetadataCategoryKeys: [manual.MetadataKey!],
                drawCount: 1, drawOncePerTurn: false,
                effectProfile: new(EffectSource.Custom, externalId, 7, "Saved plan", EffectSupport.Partial, "Saved limitation"));
            using Bunit.TestContext context = new();
            SessionService sessions = new(context.JSInterop.JSRuntime, new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());
            SessionState loaded = await sessions.LoadSessionAsync(sessions.SerializeSession(new() { Cards = [savedCard] }));
            CatalogArchetypeReconciler.Reconcile(loaded.Cards, catalog);
            Card repaired = loaded.Cards.Single();
            Assert.That((repaired.Id, repaired.Name, repaired.ExternalCardId, repaired.Copies, repaired.Active, repaired.EffectPlan, repaired.EffectProfile),
                Is.EqualTo((savedCard.Id, savedCard.Name, savedCard.ExternalCardId, savedCard.Copies, savedCard.Active, savedCard.EffectPlan, savedCard.EffectProfile)));
            Assert.That(repaired.ManualMetadataCategoryKeys, Is.EquivalentTo(savedCard.ManualMetadataCategoryKeys));
            Assert.That(repaired.Categories.Select(category => category.Identity), Is.EquivalentTo(
                savedCard.Categories.Select(category => category.Identity).Concat(imported.Categories
                    .Where(category => category.MetadataKey?.StartsWith("archetype:", StringComparison.Ordinal) == true
                        || category.MetadataKey?.StartsWith("catalog-id:", StringComparison.Ordinal) == true
                        || category.MetadataKey?.StartsWith("card-name:", StringComparison.Ordinal) == true
                        || category.MetadataKey?.StartsWith("mentions:", StringComparison.Ordinal) == true)
                    .Select(category => category.Identity))));
            Assert.That(repaired.Categories.All(category => category.MetadataKey?.Contains("treated-as", StringComparison.Ordinal) != true), Is.True);
        }
        Assert.That(catalog.Single(card => card.Id == 300201007).ResolvedArchetypes, Is.Null);
        Assert.That(catalog.Single(card => card.Id == 300101005).ResolvedArchetypes, Is.Null);
    }

    [Test]
    public void RepairsOnlyAutomaticArchetypesPreservingManualOverridesAndSavedPlans() {
        IReadOnlyList<CardInfo> catalog = BundledCatalog();
        Card solar = CuratedDrawEffectPresets.CreateImportedCard(catalog.Single(info => info.Id == 691925), 2, 691925)
            .WithCategories([Lightsworn, Fire, new("Custom role")]).WithName("Renamed by user").WithActive(false);
        Card manual = new([Lightsworn], externalCardId: 691925, manualMetadataCategoryKeys: [Lightsworn.MetadataKey!]);
        Card overlappingManual = new([Lightsworn], externalCardId: 77558536, manualMetadataCategoryKeys: [Lightsworn.MetadataKey!]);
        Card unknown = new([Lightsworn], name: "Solar Recharge", externalCardId: 2147483647);
        Card legacy = new([Lightsworn], name: "Solar Recharge");
        Card artwork = new([], name: "Wrong display label", externalCardId: 70781053);
        List<Card> cards = [solar, manual, overlappingManual, unknown, legacy, artwork];

        CatalogArchetypeReconciler.Reconcile(cards, catalog);

        Assert.That(cards[0].Categories.Select(category => category.Identity), Is.EquivalentTo(new[] { Fire.Identity, "user:Custom role", "metadata:catalog-id:691925", "metadata:card-name:solar%20recharge" }));
        Assert.That((cards[0].Id, cards[0].Name, cards[0].Copies, cards[0].Active, cards[0].ExternalCardId, cards[0].EffectPlan, cards[0].EffectProfile),
            Is.EqualTo((solar.Id, solar.Name, solar.Copies, solar.Active, solar.ExternalCardId, solar.EffectPlan, solar.EffectProfile)));
        Assert.That(cards[1].ManualMetadataCategoryKeys, Is.EquivalentTo(manual.ManualMetadataCategoryKeys));
        Assert.That(cards[1].Categories, Does.Contain(Lightsworn));
        Assert.That(CardSelector.CatalogIdentity(691925).Matches(cards[1]), Is.True);
        Assert.That(cards[2].ManualMetadataCategoryKeys, Does.Contain(Lightsworn.MetadataKey));
        Assert.That(cards[3], Is.SameAs(unknown));
        Assert.That(cards[4].Categories, Is.Empty);
        Assert.That(cards[4].ExternalCardId, Is.Null);
        Assert.That(cards[5].Categories.Where(category => category.MetadataKey!.StartsWith("archetype:")).Select(category => category.MetadataKey),
            Is.EqualTo(new[] { "archetype:archfiend" }));
        Assert.That(CardSelector.CatalogIdentity(70781052).Matches(cards[5]), Is.True);
        Card[] once = cards.ToArray();
        CatalogArchetypeReconciler.Reconcile(cards, catalog);
        for (int index = 0; index < cards.Count; index++) {
            Assert.That(cards[index], Is.SameAs(once[index]));
        }
    }

    [Test]
    public void TargetedIndexesKeepCanonicalPrecedenceUniqueArtworkAndExactNameRules() {
        CardInfo[] catalog = [
            new() { Id = 1, Name = "Unique", ArtworkImageIds = [10, 10, 2], ResolvedArchetypes = ["Lightsworn"] },
            new() { Id = 2, Name = "Other", ArtworkImageIds = [20], ResolvedArchetypes = [] },
            new() { Id = 3, Name = "unique", ArtworkImageIds = [30], ResolvedArchetypes = [] }
        ];
        List<Card> cards = [new([], externalCardId: 10), new([Lightsworn], externalCardId: 2),
            new([], name: "Unique"), new([Lightsworn], name: "unique"), new([Lightsworn], name: "UNIQUE"),
            new([Lightsworn], name: "Unique", externalCardId: 999)];
        Card[] original = cards.ToArray();
        CatalogArchetypeReconciler.Reconcile(cards, catalog);
        Assert.That(cards[0].Categories, Is.EqualTo(new[] { Lightsworn }));
        Assert.That(cards[1].Categories, Is.Empty, "canonical ID wins over another card's artwork ID");
        Assert.That(cards[2].Categories, Is.EqualTo(new[] { Lightsworn }));
        Assert.That(cards[3].Categories, Is.Empty);
        Assert.That(cards[4], Is.SameAs(original[4]));
        Assert.That(cards[5], Is.SameAs(original[5]), "unknown external IDs do not fall back to names");
    }

    [Test]
    public void UnknownEvidenceAndAmbiguousNamesOrArtworkNeverRewriteSavedCards() {
        CardInfo[] catalog = [
            new() { Id = 1, Name = "Twin", ArtworkImageIds = [10], ResolvedArchetypes = [] },
            new() { Id = 2, Name = "Twin", ArtworkImageIds = [10], ResolvedArchetypes = ["Archfiend"] },
            new() { Id = 3, Name = "Unverified", Archetype = "Lightsworn" }
        ];
        List<Card> cards = [new([Lightsworn], name: "Twin"), new([Lightsworn], externalCardId: 10), new([Lightsworn], externalCardId: 3)];
        Card[] original = cards.ToArray();
        CatalogArchetypeReconciler.Reconcile(cards, catalog);
        for (int index = 0; index < cards.Count; index++) {
            Assert.That(cards[index], Is.SameAs(original[index]));
        }
    }

    [Test]
    public async Task ShareAndAutosaveRoundTripsKeepCorrectionAndExactProbability() {
        using Bunit.TestContext context = new();
        SessionService sessions = new(context.JSInterop.JSRuntime, new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());
        SessionState session = new() {
            Cards = [new([Lightsworn], name: "Solar Recharge", externalCardId: 691925),
                new([Lightsworn], name: "Charge of the Light Brigade", externalCardId: 94886282),
                new([Lightsworn], name: "Raiden, Hand of the Lightsworn", externalCardId: 77558536)],
            Combos = [new([new(Lightsworn, 1, 1)], "Lightsworn")], HandSize = 1, CalculationModel = CalculationModel.OpeningHand
        };
        ProbabilityCalculatorService engine = new();
        Assert.That(engine.CalculateProbabilityResults(session.Cards, session.Combos, 1).TotalProbability, Is.EqualTo(1));
        CatalogArchetypeReconciler.Reconcile(session.Cards, BundledCatalog());
        double oracle = SmallDeckOracle.EnumerateProbability(session.Cards, session.Combos, 1);
        Assert.That(oracle, Is.EqualTo(1d / 3));
        Assert.That(engine.CalculateProbabilityResults(session.Cards, session.Combos, 1).TotalProbability, Is.EqualTo(oracle));
        string corrected = sessions.SerializeSession(session);
        string link = SessionShareCodec.CreateLink("https://calculator.test/", corrected);
        SessionState shared = await sessions.LoadSessionAsync(SessionShareCodec.Decode(link[link.IndexOf('#')..]));
        SessionState recovered = await sessions.LoadSessionAsync(sessions.SerializeSession(shared));
        CatalogArchetypeReconciler.Reconcile(recovered.Cards, BundledCatalog());
        Assert.That(sessions.SerializeSession(recovered), Is.EqualTo(corrected));
        Assert.That(engine.CalculateProbabilityResults(recovered.Cards, recovered.Combos, 1).TotalProbability, Is.EqualTo(oracle));
        Assert.That(recovered.SchemaVersion, Is.EqualTo(SessionState.CurrentSchemaVersion));
    }
}
