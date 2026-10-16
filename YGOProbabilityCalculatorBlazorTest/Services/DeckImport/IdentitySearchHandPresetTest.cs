using System.Numerics;
using System.Text.Json;
using Microsoft.JSInterop;
using Moq;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;
using YGOProbabilityCalculatorBlazor.Services.DeckImport;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.Session;

namespace YGOProbabilityCalculatorBlazorTest.Services.DeckImport;

[TestFixture]
public sealed class IdentitySearchHandPresetTest {
    private static readonly CardEffectRegistry Registry = new([
        new NightTrainBlueTravelerEffect(),
        new ExceptionalScheduleEffect(),
        new LunalightBlackSheepEffect()
    ]);
    private static readonly CategoryBase Goal = new("Goal");

    [Test]
    public void DefinitionsUseCanonicalCatalogAndArtworkIdentity() {
        using JsonDocument catalog = JsonDocument.Parse(File.ReadAllText(FindCatalogPath()));
        Dictionary<int, JsonElement> cardsById = catalog.RootElement.GetProperty("cards")
            .EnumerateArray()
            .ToDictionary(card => card.GetProperty("id").GetInt32());

        Assert.That(Registry.All, Has.Count.EqualTo(3));
        foreach (MaintainedCardEffect effect in Registry.All) {
            Assert.That(cardsById.TryGetValue(effect.CanonicalCardId, out JsonElement catalogCard), Is.True,
                $"Catalog does not contain canonical ID {effect.CanonicalCardId} ({effect.Name}).");
            Assert.Multiple(() => {
                Assert.That(catalogCard.GetProperty("name").GetString(), Is.EqualTo(effect.Name));
                Assert.That(catalogCard.GetProperty("canonicalCardId").GetInt32(), Is.EqualTo(effect.CanonicalCardId));
                Assert.That(catalogCard.GetProperty("artworkMetadataKnown").GetBoolean(), Is.True);
                Assert.That(catalogCard.GetProperty("artworkImageIds").EnumerateArray().Select(imageId => imageId.GetInt32()),
                    Does.Contain(effect.CanonicalCardId));
                Assert.That(Registry.Find(effect.CanonicalCardId), Is.EqualTo(effect));
            });
        }

        Assert.Multiple(() => {
            Assert.That(Registry.Find(81101309)?.Name, Is.EqualTo("Night Train Blue Traveler"));
            Assert.That(Registry.Find(52782439)?.Name, Is.EqualTo("Exceptional Schedule"));
            Assert.That(Registry.Find(11317977)?.Name, Is.EqualTo("Lunalight Black Sheep"));
        });
    }

    [Test]
    public void CatalogIdentityMatchesAnAlternateArtworkPasscode() {
        CardInfo info = CatalogInfo("Polymerization");
        int alternateArtworkId = info.ArtworkImageIds.Single(imageId => imageId != info.CanonicalCardId);
        Card alternateArtwork = new(CardPropertyProvider.GetCategories(info), name: info.Name,
            externalCardId: alternateArtworkId);

        Assert.That(CardSelector.CatalogIdentity(info.CanonicalCardId!.Value).Matches(alternateArtwork), Is.True);
    }

    [Test]
    public void NightTrainUsesCanonicalNamedTargetsAndLeavesOutItsOncePerDuelEffect() {
        Card source = Imported("Night Train Blue Traveler");
        CardSelector selector = source.Search!.Selector;
        Card trainConnection = Objective("Train Connection");
        Card switchyard = Objective("Revolving Switchyard");
        Card sameNameOnly = new([], name: "Train Connection", externalCardId: 90000101);
        Card manualIdentity = ManualIdentityTarget(60879050, "Unverified Train Connection", 90000102);

        Assert.Multiple(() => {
            Assert.That(source.Search.Matches(trainConnection), Is.True);
            Assert.That(source.Search.Matches(switchyard), Is.True);
            Assert.That(source.Search.Matches(Objective("Special Schedule")), Is.False);
            Assert.That(source.Search.Matches(sameNameOnly), Is.False, "A display name alone is not a canonical card identity.");
            Assert.That(source.Search.Matches(manualIdentity), Is.False, "A manually added identity property is not objective metadata.");
            Assert.That((source.OncePerTurn, source.Search.ObjectiveMetadataOnly), Is.EqualTo((true, true)));
            Assert.That(Registry.Describe(source).Support, Is.EqualTo(EffectSupport.ModeledWithinRules));
            Assert.That(Registry.Describe(source).Limitations, Does.Contain("Graveyard"));
            Assert.That(selector, Is.Not.EqualTo(CardSelector.Any));
        });
    }

    [Test]
    public void ExceptionalScheduleSearchesOnlyItsTwoCanonicalTargets() {
        Card source = Imported("Exceptional Schedule");

        Assert.Multiple(() => {
            Assert.That(source.Search!.Matches(Objective("Special Schedule")), Is.True);
            Assert.That(source.Search.Matches(Objective("Urgent Schedule")), Is.True);
            Assert.That(source.Search.Matches(Objective("Train Connection")), Is.False);
            Assert.That(source.OncePerTurn, Is.True);
            Assert.That(source.DrawCount, Is.Null);
            Assert.That(Registry.Describe(source).Limitations, Does.Contain("Diagram Token"));
        });
    }

    [Test]
    public void BlackSheepUsesPolymerizationPermanentNameMetadataWithoutFuzzyNameMatches() {
        Card source = Imported("Lunalight Black Sheep");
        CardSelector selector = source.Search!.Selector;
        Card polymerization = Objective("Polymerization");
        Card fusionSubstitute = Objective("Fusion Substitute");
        Card greaterPolymerization = Objective("Greater Polymerization");
        Card superPolymerization = Objective("Super Polymerization");

        Assert.Multiple(() => {
            Assert.That(source.Search.Matches(polymerization), Is.True);
            Assert.That(source.Search.Matches(fusionSubstitute), Is.True,
                "Fusion Substitute is permanently treated as Polymerization in objective catalog metadata.");
            Assert.That(source.Search.Matches(greaterPolymerization), Is.False);
            Assert.That(source.Search.Matches(superPolymerization), Is.False);
            Assert.That(source.Search.Matches(ManualNameTarget(selector, "Unverified Polymerization", 90000103)), Is.False,
                "A manually added permanent-name label cannot qualify as a Deck search target.");
            Assert.That(source.OncePerTurn, Is.False);
            Assert.That(source.PreDrawRemovalCount, Is.EqualTo(0), "The activating source is consumed by the search operation.");
        });
    }

    [Test]
    public async Task ImportSelectionRestoreAndSessionRoundTripPreserveEachSearchPlan() {
        SessionService codec = new(Mock.Of<IJSRuntime>(), new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());
        foreach (MaintainedCardEffect effect in Registry.All) {
            CardInfo info = CatalogInfo(effect.Name);
            Card imported = Registry.CreateImportedCard(info, 2, info.Id);
            Card previous = new([], id: "previous", externalCardId: 99999999);
            Card replacement = new(CardPropertyProvider.GetCategories(info), 2, info.Name, id: "stable", externalCardId: info.Id);
            Card selected = Registry.ApplyForExplicitSelection(replacement, previous, info);
            Card modified = imported.WithOncePerTurn(!effect.Plan.OncePerTurn);
            Card restored = Registry.RestoreBuiltIn(modified);
            SessionState state = new() { Cards = [imported] };
            SessionState loaded = await codec.LoadSessionAsync(codec.SerializeSession(state));
            Card loadedCard = loaded.Cards.Single();

            Assert.Multiple(() => {
                Assert.That(imported.EffectPlan, Is.EqualTo(effect.Plan));
                Assert.That(selected.EffectPlan, Is.EqualTo(effect.Plan));
                Assert.That(selected.Id, Is.EqualTo("stable"));
                Assert.That(restored.EffectPlan, Is.EqualTo(effect.Plan));
                Assert.That(loadedCard.EffectPlan, Is.EqualTo(effect.Plan));
                Assert.That(loadedCard.EffectProfile, Is.EqualTo(effect.Profile));
                Assert.That(Registry.RestoreBuiltIn(loadedCard).EffectPlan, Is.EqualTo(effect.Plan));
            });
        }
    }

    [TestCase(CalculationModel.Fast)]
    [TestCase(CalculationModel.Exact)]
    public void NightTrainNamedSearchMatchesSmallDeckProbability(CalculationModel mode) {
        Card source = Imported("Night Train Blue Traveler");
        List<Card> deck = [
            source,
            GoalCard("Train Connection"),
            GoalCard("Revolving Switchyard"),
            new([], 2, "Filler", id: "filler")
        ];
        List<Combo> combos = [new([new(Goal, 1, 1)], "Access a named target")];

        ProbabilityCalculationResult result = Calculate(deck, combos, 1, mode);

        AssertProbability(result, 3, 5);
    }

    [TestCase(CalculationModel.Fast)]
    [TestCase(CalculationModel.Exact)]
    public void ExceptionalScheduleSearchMatchesSmallDeckProbability(CalculationModel mode) {
        Card source = Imported("Exceptional Schedule");
        List<Card> deck = [
            source,
            GoalCard("Special Schedule"),
            GoalCard("Urgent Schedule"),
            new([], 2, "Filler", id: "filler")
        ];
        List<Combo> combos = [new([new(Goal, 1, 1)], "Access a Schedule")];

        ProbabilityCalculationResult result = Calculate(deck, combos, 1, mode);

        AssertProbability(result, 3, 5);
    }

    [TestCase(CalculationModel.Fast)]
    [TestCase(CalculationModel.Exact)]
    public void BlackSheepSearchMatchesSmallDeckProbabilityAndRejectsFuzzyTargets(CalculationModel mode) {
        Card source = Imported("Lunalight Black Sheep");
        List<Card> deck = [
            source,
            GoalCard("Polymerization"),
            GoalCard("Fusion Substitute"),
            Objective("Greater Polymerization"),
            new([], id: "filler")
        ];
        List<Combo> combos = [new([new(Goal, 1, 1)], "Access Polymerization")];

        ProbabilityCalculationResult result = Calculate(deck, combos, 1, mode);

        AssertProbability(result, 3, 5);
    }

    private static ProbabilityCalculationResult Calculate(List<Card> deck, List<Combo> combos, int handSize, CalculationModel mode) =>
        new ProbabilityCalculatorService().CalculateProbabilityResults(deck, combos, handSize, null,
            CalculationWorkPolicy.Default, new(Mode: mode));

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

    private static Card Objective(string name) {
        CardInfo info = CatalogInfo(name);
        return new(CardPropertyProvider.GetCategories(info), name: info.Name, id: info.Id.ToString(), externalCardId: info.Id);
    }

    private static Card GoalCard(string name) {
        CardInfo info = CatalogInfo(name);
        return new(CardPropertyProvider.GetCategories(info).Append(Goal), name: info.Name, id: info.Id.ToString(), externalCardId: info.Id);
    }

    private static Card ManualIdentityTarget(int canonicalId, string name, int externalId) =>
        ManualMetadataTarget(CardSelector.CatalogIdentity(canonicalId), name, externalId);

    private static Card ManualNameTarget(CardSelector selector, string name, int externalId) =>
        ManualMetadataTarget(selector, name, externalId);

    private static Card ManualMetadataTarget(CardSelector selector, string name, int externalId) {
        string identity = selector.CategoryIdentities.Single();
        string metadataKey = identity["metadata:".Length..];
        CategoryBase spoofed = new("Unverified catalog property", CategorySource.Metadata, metadataKey);
        Card target = new([], name: name, externalCardId: externalId);
        return target.WithManualMetadataCategory(spoofed);
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
