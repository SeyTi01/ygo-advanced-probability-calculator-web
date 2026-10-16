using System.Net;
using System.Text.Json;
using Moq;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;
using YGOProbabilityCalculatorBlazor.Services.DeckImport;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public sealed class Issue215SearchPresetFractionTest {
    private const int FillerId = 46986414;
    private static readonly JsonSerializerOptions CatalogJsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly PresetCase[] Presets = [
        new(new KetuDracotailEffect(), 75003700),
        new(new MitsurugiPrayersEffect(), 13332685),
        new(new SpentaTheMagistusSealerEffect(), 9482987),
        new(new PrimiteLordlyLodeEffect(), 63198739),
        new(new WantedSeekerOfSinfulSpoilsEffect(), 72270339),
        new(new SpellPowerMasteryEffect(), 39910367),
        new(new EmpireOfEndymionEffect(), 96228804),
        new(new FairyTailBallEffect(), 19144622),
        new(new FutureSilenceEffect(), 92110878)
    ];
    private static readonly int[] CatalogFixtureIds = [
        6153210, 75003700,
        45171524, 13332685,
        42544773, 9482987,
        56506740, 63198739,
        80845034, 72270339,
        38943357, 39910367,
        34041788, 96228804,
        56725612, 19144622,
        78679226, 92110878,
        FillerId
    ];
    private static readonly CardEffectRegistry Registry = new(Presets.Select(preset => preset.Definition));
    private static readonly Lazy<Task<IReadOnlyList<CardInfo>>> Catalog = new(LoadCatalogCardsAsync);

    [Test]
    public async Task EveryPresetReturnsTwoFifthsInFastAndExactModesWithARealImportedTarget() {
        IReadOnlyList<CardInfo> catalog = await Catalog.Value;
        ProbabilityCalculatorService calculator = new();

        foreach (PresetCase preset in Presets) {
            Card source = Import(catalog, preset.Definition.CanonicalCardId);
            Card target = Import(catalog, preset.PositiveTargetId);
            Card filler = Import(catalog, FillerId, copies: 3);
            Combo combo = new([], "Find the imported target", cards: [new(target.Id, 1, 1)]);
            List<Card> deck = [source, target, filler];

            Assert.That(source.Search!.Matches(target), Is.True, preset.Definition.Name + " positive catalog target");
            Assert.That(source.Search.Matches(filler), Is.False, preset.Definition.Name + " negative catalog filler");

            foreach (CalculationModel mode in new[] { CalculationModel.Fast, CalculationModel.Exact }) {
                ProbabilityCalculationResult result = calculator.CalculateProbabilityResults(
                    deck, [combo], 1, [], CalculationWorkPolicy.Default, options: new(Mode: mode));

                Assert.That(result.Contract!.RequestedMode, Is.EqualTo(mode), preset.Definition.Name + " requested mode");
                Assert.That(result.Contract.Fractions!.Total, Is.EqualTo(new ProbabilityFraction("2", "5")),
                    preset.Definition.Name + " union fraction in " + mode);
                Assert.That(result.Contract.Fractions.Combos.Single(), Is.EqualTo(new ProbabilityFraction("2", "5")),
                    preset.Definition.Name + " combo fraction in " + mode);
            }
        }
    }

    [Test]
    public async Task SeparateRowsWithTheSameDisplayNameKeepIndependentOncePerTurnSearches() {
        IReadOnlyList<CardInfo> catalog = await Catalog.Value;
        ProbabilityCalculatorService calculator = new();

        foreach (PresetCase preset in Presets) {
            Card sourceInfoCard = Import(catalog, preset.Definition.CanonicalCardId);
            Card secondSource = Import(catalog, preset.Definition.CanonicalCardId);
            Card targets = Import(catalog, preset.PositiveTargetId, copies: 2);
            Card filler = Import(catalog, FillerId);
            Combo combo = new([], "Find both target copies", cards: [new(targets.Id, 2, 2)]);
            List<Card> deck = [sourceInfoCard, secondSource, targets, filler];

            Assert.Multiple(() => {
                Assert.That(sourceInfoCard.Name, Is.EqualTo(secondSource.Name), preset.Definition.Name + " display names");
                Assert.That(sourceInfoCard.Id, Is.Not.EqualTo(secondSource.Id), preset.Definition.Name + " distinct entry IDs");
                Assert.That(sourceInfoCard.OncePerTurn, Is.True, preset.Definition.Name + " first entry OPT");
                Assert.That(secondSource.OncePerTurn, Is.True, preset.Definition.Name + " second entry OPT");
            });

            foreach (CalculationModel mode in new[] { CalculationModel.Fast, CalculationModel.Exact }) {
                ProbabilityCalculationResult result = calculator.CalculateProbabilityResults(
                    deck, [combo], 3, [], CalculationWorkPolicy.Default, options: new(Mode: mode));

                Assert.That(result.Contract!.Fractions!.Total, Is.EqualTo(new ProbabilityFraction("1", "1")),
                    preset.Definition.Name + " independent same-name activations in " + mode);
            }
        }
    }

    private static Card Import(IReadOnlyList<CardInfo> catalog, int id, int copies = 1) =>
        Registry.CreateImportedCard(catalog.Single(card => card.Id == id), copies, id);

    private static async Task<IReadOnlyList<CardInfo>> LoadCatalogCardsAsync() {
        using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(FindCatalogPath()));
        CardInfo[] cards = document.RootElement.GetProperty("cards").EnumerateArray()
            .Where(card => CatalogFixtureIds.Contains(card.GetProperty("id").GetInt32()))
            .Select(card => card.Deserialize<CardInfo>(CatalogJsonOptions)!)
            .ToArray();

        if (cards.Length != CatalogFixtureIds.Length) {
            throw new InvalidDataException("The checked-in catalog is missing an issue 215 search fixture.");
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

    private sealed record PresetCase(ICardEffectDefinition Definition, int PositiveTargetId);

    private sealed class CatalogResponseHandler(string catalogJson) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("/data/card-catalog.v1.json", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(catalogJson) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
