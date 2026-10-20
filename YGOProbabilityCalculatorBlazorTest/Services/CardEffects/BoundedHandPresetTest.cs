using System.Net;
using System.Text.Json;
using Moq;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;
using YGOProbabilityCalculatorBlazor.Services.DeckImport;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.CardEffects;

[TestFixture]
public sealed class BoundedHandPresetTest {
    private const int DarkMagicianId = 46986414;
    private static readonly JsonSerializerOptions CatalogJsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly ICardEffectDefinition[] Definitions = [
        new BrandedOpeningEffect(),
        new FiendsmithTractEffect(),
        new KewlTuneSynchroEffect(),
        new RadiantTyphoonChantEffect()
    ];
    private static readonly CardEffectRegistry Registry = new(Definitions);
    private static readonly int[] CatalogFixtureIds = [
        36637374, 62962630,
        98567237, 60764609, 4779091, 97268402,
        78058681, 89392810,
        67115133, 54143349, 85315450, 25940932, 5318639,
        DarkMagicianId
    ];
    private static readonly Lazy<Task<IReadOnlyList<CardInfo>>> Catalog = new(LoadCatalogCardsAsync);

    [Test]
    public async Task ImportedSearchTargetsUseObjectiveCatalogMetadata() {
        IReadOnlyList<CardInfo> catalog = await Catalog.Value;
        SearchEffectPlan brandedOpening = Search(36637374);
        Card brandedMonster = Import(catalog, 62962630);
        Card darkMagician = Import(catalog, DarkMagicianId);

        Assert.That(brandedOpening.Matches(brandedMonster), Is.True, "Aluber is a catalog Despia Monster.");
        Assert.That(brandedOpening.Matches(darkMagician), Is.False, "An unrelated Monster is not a Despia target.");
        Assert.That(brandedOpening.Matches(darkMagician.WithName("Aluber the Jester of Despia")), Is.False,
            "Changing the workspace display name cannot create objective Despia membership.");

        SearchEffectPlan tract = Search(98567237);
        Card lightFiend = Import(catalog, 60764609);
        Card darkFiend = Import(catalog, 4779091);
        Card lightSpellcaster = Import(catalog, 97268402);

        Assert.Multiple(() => {
            Assert.That(tract.Matches(lightFiend), Is.True, "Fiendsmith Engraver is a LIGHT Fiend Monster.");
            Assert.That(tract.Matches(darkFiend), Is.False, "A DARK Fiend does not match the LIGHT requirement.");
            Assert.That(tract.Matches(lightSpellcaster), Is.False, "A LIGHT Spellcaster does not match the Fiend requirement.");
        });

        SearchEffectPlan kewlTune = Search(78058681);
        Card synchro = Import(catalog, 78058681);
        Card otherKewlTune = Import(catalog, 89392810);
        Card spoofedKewlTuneName = darkMagician.WithName("Kewl Tune Reco");

        Assert.Multiple(() => {
            Assert.That(kewlTune.Matches(otherKewlTune), Is.True, "A different Kewl Tune card is searchable.");
            Assert.That(kewlTune.Matches(synchro), Is.False, "Kewl Tune Synchro excludes its own catalog identity.");
            Assert.That(kewlTune.Matches(synchro.WithName("Renamed source")), Is.False,
                "Renaming the source does not remove its objective identity exclusion.");
            Assert.That(kewlTune.Matches(spoofedKewlTuneName), Is.False,
                "Renaming an unrelated card does not create objective Kewl Tune membership.");
            Assert.That(kewlTune.Matches(otherKewlTune.WithName("Kewl Tune Synchro")), Is.True,
                "A different Kewl Tune identity remains searchable even if its display name is edited to the source name.");
        });

        SearchEffectPlan chant = Search(67115133);
        Card radiantTyphoonMonster = Import(catalog, 54143349);
        Card wrongLevel = Import(catalog, 85315450);
        Card wrongType = Import(catalog, 25940932);
        Card mysticalSpaceTyphoon = Import(catalog, 5318639);
        Card spoofedMysticalSpaceTyphoon = darkMagician.WithName("Mystical Space Typhoon");
        IReadOnlyList<SearchEffectMode> modes = chant.Modes!;

        Assert.Multiple(() => {
            Assert.That(modes, Has.Count.EqualTo(2));
            Assert.That(modes[0].ActivationLimit, Is.EqualTo(1));
            Assert.That(modes[1].ActivationLimit, Is.EqualTo(1));
            Assert.That(chant.Matches(radiantTyphoonMonster), Is.True, "Radiant Typhoon Eldam is a Level 3 Monster.");
            Assert.That(chant.Matches(wrongLevel), Is.False, "The Level 9 Radiant Typhoon Monster is outside the level range.");
            Assert.That(chant.Matches(wrongType), Is.False, "A Radiant Typhoon Spell is not a Monster target.");
            Assert.That(chant.Matches(mysticalSpaceTyphoon), Is.True, "Mystical Space Typhoon is the second named target mode.");
            Assert.That(chant.Matches(spoofedMysticalSpaceTyphoon), Is.False,
                "A mutable display-name spoof cannot satisfy the second mode's catalog identity.");
            Assert.That(chant.Matches(radiantTyphoonMonster, modes[0].Selector), Is.True);
            Assert.That(chant.Matches(radiantTyphoonMonster, modes[1].Selector), Is.False);
            Assert.That(chant.Matches(mysticalSpaceTyphoon, modes[0].Selector), Is.False);
            Assert.That(chant.Matches(mysticalSpaceTyphoon, modes[1].Selector), Is.True);
        });
    }

    [Test]
    public async Task ExplicitSelectionAndRestoreRetainEachMaintainedSearchPlan() {
        IReadOnlyList<CardInfo> catalog = await Catalog.Value;

        foreach (ICardEffectDefinition definition in Definitions) {
            CardInfo sourceInfo = CatalogCard(catalog, definition.CanonicalCardId);
            Card imported = Registry.CreateImportedCard(sourceInfo, 1, sourceInfo.Id);
            Card selected = Registry.ApplyForExplicitSelection(
                imported.WithName("Edited workspace label"), imported, sourceInfo);
            Card restored = Registry.RestoreBuiltIn(imported.WithSearch(null));

            Assert.Multiple(() => {
                Assert.That(imported.EffectProfile.Source, Is.EqualTo(EffectSource.BuiltIn), definition.Name);
                Assert.That(selected.Search, Is.EqualTo(imported.Search), definition.Name + " selected");
                Assert.That(restored.Search, Is.EqualTo(imported.Search), definition.Name + " restored");
                Assert.That(restored.EffectProfile.Revision, Is.EqualTo(definition.Revision), definition.Name + " revision");
            });
        }
    }

    [Test]
    public async Task SearchActivationConsumesItsSourceAndReturnsExpectedFastAndExactFractions() {
        IReadOnlyList<CardInfo> catalog = await Catalog.Value;
        ProbabilityCalculatorService calculator = new();

        foreach (ICardEffectDefinition definition in Definitions) {
            CardInfo sourceInfo = CatalogCard(catalog, definition.CanonicalCardId);
            Card source = Registry.CreateImportedCard(sourceInfo, 1, sourceInfo.Id);
            Card filler = Import(catalog, DarkMagicianId, copies: 3);
            int[] targetIds = definition.CanonicalCardId switch {
                36637374 => [62962630],
                98567237 => [60764609],
                78058681 => [89392810],
                67115133 => [54143349, 5318639],
                _ => throw new ArgumentOutOfRangeException(nameof(definition))
            };

            foreach (int targetId in targetIds) {
                Card target = Import(catalog, targetId);
                List<Card> deck = [source, target, filler];
                int handSize = definition.CanonicalCardId is 36637374 or 98567237 ? 2 : 1;
                ProbabilityFraction targetFraction = handSize == 2
                    ? new("7", "10")
                    : new("2", "5");
                Combo targetCombo = new([], "Find target", cards: [new(target.Id, 1, 1)]);

                foreach (CalculationModel mode in new[] { CalculationModel.Fast, CalculationModel.Exact }) {
                    ProbabilityCalculationResult result = calculator.CalculateProbabilityResults(
                        deck, [targetCombo], handSize, [], CalculationWorkPolicy.Default, options: new(Mode: mode));

                    Assert.That(result.Contract!.Fractions!.Total, Is.EqualTo(targetFraction),
                        definition.Name + " direct or searched target in " + mode);
                }

                Combo sourceAndTargetCombo = new([], "Keep source and target", cards: [
                    new(source.Id, 1, 1),
                    new(target.Id, 1, 1)
                ]);

                foreach (CalculationModel mode in new[] { CalculationModel.Fast, CalculationModel.Exact }) {
                    ProbabilityCalculationResult result = calculator.CalculateProbabilityResults(
                        deck, [sourceAndTargetCombo], 2, [], CalculationWorkPolicy.Default, options: new(Mode: mode));

                    Assert.That(result.Contract!.Fractions!.Total, Is.EqualTo(new ProbabilityFraction("1", "10")),
                        definition.Name + " source is consumed in " + mode);
                }
            }
        }
    }

    private static SearchEffectPlan Search(int id) => Registry.Find(id)!.Plan.Search!;

    private static Card Import(IReadOnlyList<CardInfo> catalog, int id, int copies = 1) =>
        Registry.CreateImportedCard(CatalogCard(catalog, id), copies, id);

    private static CardInfo CatalogCard(IReadOnlyList<CardInfo> catalog, int id) =>
        catalog.Single(card => card.Id == id);

    private static async Task<IReadOnlyList<CardInfo>> LoadCatalogCardsAsync() {
        using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(FindCatalogPath()));
        CardInfo[] cards = document.RootElement.GetProperty("cards").EnumerateArray()
            .Where(card => CatalogFixtureIds.Contains(card.GetProperty("id").GetInt32()))
            .Select(card => card.Deserialize<CardInfo>(CatalogJsonOptions)!)
            .ToArray();

        if (cards.Length != CatalogFixtureIds.Length) {
            throw new InvalidDataException("The checked-in catalog is missing a bounded hand preset fixture.");
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
