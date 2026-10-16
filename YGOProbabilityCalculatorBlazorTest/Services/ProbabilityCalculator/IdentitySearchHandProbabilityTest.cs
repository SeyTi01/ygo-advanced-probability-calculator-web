using System.Numerics;
using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;
using YGOProbabilityCalculatorBlazor.Services.DeckImport;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public sealed class IdentitySearchHandProbabilityTest {
    private static readonly CardEffectRegistry Registry = new([
        new NightTrainBlueTravelerEffect(),
        new ExceptionalScheduleEffect(),
        new LunalightBlackSheepEffect()
    ]);

    [TestCase(CalculationModel.Fast)]
    [TestCase(CalculationModel.Exact)]
    public void NightTrainSearchAccessesItsActualTargetWithTwoFifthsProbability(CalculationModel mode) {
        Card source = Imported("Night Train Blue Traveler");
        Card target = Imported("Train Connection");

        AssertProbability(CalculateAccess(source, target, mode), 2, 5);
    }

    [TestCase(CalculationModel.Fast)]
    [TestCase(CalculationModel.Exact)]
    public void ExceptionalScheduleSearchesItsActualTargetWithTwoFifthsProbability(CalculationModel mode) {
        Card source = Imported("Exceptional Schedule");
        Card target = Imported("Special Schedule");

        AssertProbability(CalculateAccess(source, target, mode), 2, 5);
    }

    [TestCase(CalculationModel.Fast)]
    [TestCase(CalculationModel.Exact)]
    public void BlackSheepSearchesPolymerizationAndFusionSubstituteWithEquivalentProbability(CalculationModel mode) {
        Card source = Imported("Lunalight Black Sheep");
        ProbabilityCalculationResult polymerization = CalculateAccess(source, Imported("Polymerization"), mode);
        ProbabilityCalculationResult fusionSubstitute = CalculateAccess(source, Imported("Fusion Substitute"), mode);

        AssertProbability(polymerization, 2, 5);
        AssertProbability(fusionSubstitute, 2, 5);
        ProbabilityFraction first = polymerization.Contract!.Fractions!.Total;
        ProbabilityFraction second = fusionSubstitute.Contract!.Fractions!.Total;
        Assert.That(BigInteger.Parse(first.Numerator) * BigInteger.Parse(second.Denominator),
            Is.EqualTo(BigInteger.Parse(second.Numerator) * BigInteger.Parse(first.Denominator)));
    }

    private static ProbabilityCalculationResult CalculateAccess(Card source, Card target, CalculationModel mode) {
        List<Card> deck = [source, target, new([], 3, "Filler", id: "filler")];
        List<Combo> combos = [new([], $"Access {target.Name}", cards: [new(target.Id, 1, 1)])];
        return new ProbabilityCalculatorService().CalculateProbabilityResults(deck, combos, 1, null,
            CalculationWorkPolicy.Default, new(Mode: mode));
    }

    private static void AssertProbability(ProbabilityCalculationResult result, int numerator, int denominator) {
        Assert.That(result.Contract!.Completion, Is.EqualTo(CalculationCompletion.Success));
        Assert.That(result.Contract.Accuracy, Is.EqualTo(CalculationAccuracy.Exact));
        ProbabilityFraction actual = result.Contract.Fractions!.Total;
        Assert.That(BigInteger.Parse(actual.Numerator) * denominator,
            Is.EqualTo(new BigInteger(numerator) * BigInteger.Parse(actual.Denominator)));
    }

    private static Card Imported(string name) {
        CardInfo info = CatalogInfo(name);
        return Registry.CreateImportedCard(info, 1, info.Id);
    }

    private static CardInfo CatalogInfo(string name) {
        using JsonDocument catalog = JsonDocument.Parse(File.ReadAllText(FindCatalogPath()));
        JsonElement card = catalog.RootElement.GetProperty("cards").EnumerateArray()
            .Single(entry => entry.GetProperty("name").GetString() == name);
        return JsonSerializer.Deserialize<CardInfo>(card.GetRawText(), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException($"Could not deserialize catalog metadata for {name}.");
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
}
