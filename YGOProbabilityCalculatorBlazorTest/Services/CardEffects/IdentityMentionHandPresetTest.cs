using System.Net;
using System.Text.Json;
using Microsoft.JSInterop;
using Moq;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;
using YGOProbabilityCalculatorBlazor.Services.DeckImport;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.Session;

namespace YGOProbabilityCalculatorBlazorTest.Services.CardEffects;

public sealed class IdentityMentionHandPresetTest {
    private const string RegulusName = "Regulus, the Prince of Endymion";
    private static readonly JsonSerializerOptions CatalogJsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly int[] CatalogFixtureIds = [
        34041788, 56725612, 78679226,
        96228804, 22623509, 59106048, 40732515,
        19144622, 91957038, 92110878, 46986414
    ];
    private static readonly Lazy<Task<IReadOnlyList<CardInfo>>> Catalog = new(LoadCatalogCardsAsync);

    private static readonly ICardEffectDefinition[] Definitions = [
        new EmpireOfEndymionEffect(),
        new FairyTailBallEffect(),
        new FutureSilenceEffect()
    ];

    private static readonly (int Id, string Name)[] Identities = [
        (34041788, "Empire of Endymion"),
        (56725612, "Fairy Tail Ball"),
        (78679226, "Future Silence")
    ];

    [Test]
    public async Task DefinitionsUseVerifiedCatalogIdentitiesAndOncePerEntryObjectiveSearchPlans() {
        CardEffectRegistry registry = CreateRegistry();
        IReadOnlyList<CardInfo> catalog = await Catalog.Value;

        Assert.That(registry.All.Select(effect => (effect.CanonicalCardId, effect.Name)), Is.EqualTo(Identities));

        foreach ((int id, string name) in Identities) {
            CardInfo info = CatalogCard(catalog, id);

            Assert.Multiple(() => {
                Assert.That(info.CanonicalCardId, Is.EqualTo(id), name);
                Assert.That(info.ArtworkImageIds, Is.EqualTo(new[] { id }), name);
                Assert.That(registry.Find(info), Is.Not.Null, name);
            });
        }

        Assert.That(Definitions.SelectMany(definition => definition.Aliases), Is.Empty,
            "The verified catalog has no additional passcode aliases for these source entries.");

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
    public async Task ImportedSelectedRestoredSavedAndWorkerPlansRetainIdentityAndMentionSelectors() {
        CardEffectRegistry registry = CreateRegistry();
        IReadOnlyList<CardInfo> catalog = await Catalog.Value;
        SessionService codec = new(Mock.Of<IJSRuntime>(), new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());

        foreach ((int id, string name) in Identities) {
            CardInfo info = CatalogCard(catalog, id);
            Card imported = registry.CreateImportedCard(info, 1, info.Id);
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
    public async Task RegulusAndFairyTailTargetsUseObjectiveIdentityAndMentionMetadata() {
        CardEffectRegistry registry = CreateRegistry();
        IReadOnlyList<CardInfo> catalog = await Catalog.Value;
        SearchEffectPlan empire = registry.Find(34041788)!.Plan.Search!;
        SearchEffectPlan fairyTail = registry.Find(56725612)!.Plan.Search!;

        Card regulus = Import(registry, catalog, 96228804);
        Card verre = Import(registry, catalog, 22623509);
        Card witchcrafterSeed = Import(registry, catalog, 59106048);
        Card endymionSpell = Import(registry, catalog, 34041788);
        Card nonMentioningEndymion = Import(registry, catalog, 40732515);
        Card darkMagician = Import(registry, catalog, 46986414);

        Assert.That(empire.Matches(regulus), Is.True, "The named Regulus target is accepted by its catalog identity.");
        Assert.That(empire.Matches(verre), Is.True, "A Monster whose verified card text mentions Regulus is accepted.");
        Assert.That(empire.Matches(witchcrafterSeed), Is.True, "A non-Endymion Monster is accepted when its text mentions Regulus.");
        Assert.That(empire.Matches(endymionSpell), Is.False, "A non-Monster mentioning Regulus is not a target.");
        Assert.That(empire.Matches(nonMentioningEndymion), Is.False, "An Endymion Monster without the required mention is not a target.");
        Assert.That(empire.Matches(darkMagician.WithName(RegulusName)), Is.False,
            "Renaming a different catalog card does not create Regulus identity or text metadata.");

        Card fairyMonster = Import(registry, catalog, 19144622);
        Card namedSpell = Import(registry, catalog, 91957038);
        Card fairyTailBallSpell = Import(registry, catalog, 56725612);

        Assert.That(fairyTail.Matches(fairyMonster), Is.True, "A verified Fairy Tail Monster is a target.");
        Assert.That(fairyTail.Matches(namedSpell), Is.True, "Fairy Tail Tales is included by its catalog identity.");
        Assert.That(fairyTail.Matches(fairyTailBallSpell), Is.False, "The Fairy Tail archetype condition requires a Monster.");
        Assert.That(fairyTail.Matches(darkMagician.WithName("Fairy Tail Tales")), Is.False,
            "Renaming a different card does not create Fairy Tail Tales identity.");
    }

    [Test]
    public async Task ShiningSarcophagusMentionSelectorRequiresMonsterAndIgnoresManualProxies() {
        CardEffectRegistry registry = CreateRegistry();
        IReadOnlyList<CardInfo> catalog = await Catalog.Value;
        SearchEffectPlan futureSilence = registry.Find(78679226)!.Plan.Search!;

        Card gadgetTrio = Import(registry, catalog, 92110878);
        Card futureSilenceSpell = Import(registry, catalog, 78679226);
        Card darkMagician = Import(registry, catalog, 46986414);
        CategoryBase manualMonster = CardPropertyProvider.CommonCategories.Single(category => category.MetadataKey == "kind:monster");
        Card manuallyTaggedSpell = futureSilenceSpell.WithManualMetadataCategory(manualMonster);
        Card userCategoryProxy = darkMagician.WithCategories(darkMagician.Categories.Append(new("Shining Sarcophagus")));

        Assert.That(futureSilence.Matches(gadgetTrio), Is.True, "A Monster with verified Shining Sarcophagus text is a target.");
        Assert.That(futureSilence.Matches(futureSilenceSpell), Is.False, "A Spell that mentions Shining Sarcophagus is not a target.");
        Assert.That(futureSilence.Matches(darkMagician), Is.False, "A Monster without the objective mention is not a target.");
        Assert.That(futureSilence.Matches(manuallyTaggedSpell), Is.False, "A user-added Monster property cannot make a Spell an objective Monster.");
        Assert.That(futureSilence.Matches(userCategoryProxy), Is.False, "A user category cannot satisfy an objective search.");
        Assert.That(futureSilence.Matches(darkMagician.WithName("Gadget Trio")), Is.False,
            "A mutable display name cannot satisfy a text-mention selector.");
    }

    private static CardEffectRegistry CreateRegistry() => new(Definitions);

    private static Card Import(CardEffectRegistry registry, IReadOnlyList<CardInfo> catalog, int id) =>
        registry.CreateImportedCard(CatalogCard(catalog, id), 1, id);

    private static CardInfo CatalogCard(IReadOnlyList<CardInfo> catalog, int id) =>
        catalog.Single(card => card.Id == id);

    private static async Task<IReadOnlyList<CardInfo>> LoadCatalogCardsAsync() {
        string catalogPath = FindCatalogPath();
        using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(catalogPath));
        CardInfo[] cards = document.RootElement.GetProperty("cards").EnumerateArray()
            .Where(card => CatalogFixtureIds.Contains(card.GetProperty("id").GetInt32()))
            .Select(card => card.Deserialize<CardInfo>(CatalogJsonOptions)!)
            .ToArray();

        if (cards.Length != CatalogFixtureIds.Length) {
            throw new InvalidDataException("The checked-in catalog is missing an identity/mention test fixture.");
        }

        string subsetJson = JsonSerializer.Serialize(new { version = 2, cards }, CatalogJsonOptions);
        using HttpClient httpClient = new(new CatalogResponseHandler(subsetJson)) {
            BaseAddress = new Uri("https://calculator.test/")
        };
        CardInfoService service = new(Mock.Of<ILocalStorageService>(), httpClient);

        return await service.GetCatalogCardsAsync();
    }

    private static string FindCatalogPath() {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null) {
            string path = Path.Combine(directory.FullName, "YGOProbabilityCalculatorBlazor", "wwwroot", "data", "card-catalog.v1.json");

            if (File.Exists(path)) {
                return path;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate the checked-in card catalog from the test output directory.");
    }

    private sealed class CatalogResponseHandler(string catalogJson) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("/data/card-catalog.v1.json", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(catalogJson) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
