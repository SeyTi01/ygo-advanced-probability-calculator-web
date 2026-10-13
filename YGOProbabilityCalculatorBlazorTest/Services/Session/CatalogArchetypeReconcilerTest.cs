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

        Assert.That(cards[0].Categories.Select(category => category.Identity), Is.EquivalentTo(new[] { Fire.Identity, "user:Custom role" }));
        Assert.That((cards[0].Id, cards[0].Name, cards[0].Copies, cards[0].Active, cards[0].ExternalCardId, cards[0].EffectPlan, cards[0].EffectProfile),
            Is.EqualTo((solar.Id, solar.Name, solar.Copies, solar.Active, solar.ExternalCardId, solar.EffectPlan, solar.EffectProfile)));
        Assert.That(cards[1], Is.SameAs(manual));
        Assert.That(cards[2].ManualMetadataCategoryKeys, Does.Contain(Lightsworn.MetadataKey));
        Assert.That(cards[3], Is.SameAs(unknown));
        Assert.That(cards[4].Categories, Is.Empty);
        Assert.That(cards[4].ExternalCardId, Is.Null);
        Assert.That(cards[5].Categories.Select(category => category.MetadataKey), Is.EqualTo(new[] { "archetype:archfiend" }));
        Card[] once = cards.ToArray();
        CatalogArchetypeReconciler.Reconcile(cards, catalog);
        for (int index = 0; index < cards.Count; index++) {
            Assert.That(cards[index], Is.SameAs(once[index]));
        }
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
            Combos = [new([new(Lightsworn, 1, 1)], "Lightsworn")], HandSize = 1, DrawEffectsEnabled = false
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
        Assert.That(recovered.SchemaVersion, Is.EqualTo(5));
    }
}
