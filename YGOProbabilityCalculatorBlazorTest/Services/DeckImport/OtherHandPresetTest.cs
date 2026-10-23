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
public sealed class OtherHandPresetTest {
    private static readonly CardEffectRegistry Registry = new([
        new SkyStrikerMobilizeEngageEffect(),
        new DarklordIxchelEffect(),
        new TraptantalizingTuneEffect()
    ]);
    private static readonly CategoryBase Goal = new("Goal");

    [Test]
    public void DefinitionsMatchCanonicalCatalogAndArtworkIdentity() {
        string catalogPath = FindCatalogPath();
        using JsonDocument catalog = JsonDocument.Parse(File.ReadAllText(catalogPath));
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
                    Does.Contain(effect.CanonicalCardId), "The canonical passcode must be represented in the catalog artwork identity set.");
                Assert.That(Registry.Find(effect.CanonicalCardId), Is.EqualTo(effect));
            });
        }

        Assert.Multiple(() => {
            Assert.That(Registry.Find(63166095)?.Name, Is.EqualTo("Sky Striker Mobilize - Engage!"));
            Assert.That(Registry.Find(52840267)?.Name, Is.EqualTo("Darklord Ixchel"));
            Assert.That(Registry.Find(48805472)?.Name, Is.EqualTo("Traptantalizing Tune"));
        });
    }

    [Test]
    public void EngageSearchUsesObjectiveSkyStrikerIdentityAndExcludesItsOwnCatalogName() {
        Card source = Imported("Sky Striker Mobilize - Engage!");
        Card raye = Objective("Sky Striker Ace - Raye");
        Card unrelated = Objective("Darklord Nasten");
        CardSelector skyStriker = source.Search!.Selector;

        Assert.Multiple(() => {
            Assert.That(source.Search.Matches(raye), Is.True);
            Assert.That(source.Search.Matches(unrelated), Is.False);
            Assert.That(source.Search.Matches(Objective("Sky Striker Mobilize - Engage!")), Is.False);
            Assert.That(source.OncePerTurn, Is.False);
            Assert.That(source.DrawCount, Is.Null);
            Assert.That(source.EffectProfile.Support, Is.EqualTo(EffectSupport.ModeledWithinRules));
        });

        CategoryBase archetype = source.Categories.Single(category => category.MetadataKey == "archetype:sky%20striker");
        Card manualArchetype = new Card([], name: "Unverified Engage-looking display name", externalCardId: 90000001)
            .WithManualMetadataCategory(archetype);
        Assert.That(source.Search.Matches(manualArchetype), Is.False,
            "A manually supplied archetype cannot qualify as an objective search target.");

        Card sameNameWithoutCatalogIdentity = new([archetype], name: source.Name, externalCardId: 90000002);
        Assert.That(source.Search.Matches(sameNameWithoutCatalogIdentity), Is.True,
            "A display name alone must not act as the excluded permanent card identity.");
        Assert.That(skyStriker.Kind, Is.Not.EqualTo(CardSelectorKind.Any));
    }

    [Test]
    public void IxchelAndTuneCostsRequireOneEligibleOtherPhysicalCardWithObjectiveMetadata() {
        Card ixchel = Imported("Darklord Ixchel");
        Card tune = Imported("Traptantalizing Tune");
        CardSelector ixchelCost = ixchel.PreDrawSelector;
        CardSelector tuneCost = tune.PreDrawSelector;

        Card darklordMonster = Objective("Darklord Nasten");
        Card darklordSpell = Objective("Darklord Contact");
        Card insect = Objective("Resonance Insect");
        Card plant = Objective("Angel Trumpeter");
        Card normalTrap = Objective("Reckless Greed");
        Card lowLevelPlant = Objective("Lonefire Blossom");
        Card continuousTrap = Objective("Call of the Haunted");
        Card unrelated = Objective("Sky Striker Ace - Raye");

        Assert.Multiple(() => {
            Assert.That((ixchel.DrawCount, ixchel.OncePerTurn, ixchel.PreDrawRemovalCount), Is.EqualTo(((int?)2, true, 1)));
            Assert.That(ixchelCost.Matches(darklordMonster), Is.True);
            Assert.That(ixchelCost.Matches(darklordSpell), Is.True, "The cost is any Darklord card, not only a monster.");
            Assert.That(ixchelCost.Matches(unrelated), Is.False);
            Assert.That((tune.DrawCount, tune.OncePerTurn, tune.PreDrawRemovalCount), Is.EqualTo(((int?)2, true, 1)));
            Assert.That(tuneCost.Matches(insect), Is.True);
            Assert.That(tuneCost.Matches(plant), Is.True);
            Assert.That(tuneCost.Matches(normalTrap), Is.True);
            Assert.That(tuneCost.Matches(lowLevelPlant), Is.False);
            Assert.That(tuneCost.Matches(continuousTrap), Is.False);
            Assert.That(tuneCost.Matches(unrelated), Is.False);
        });

        CategoryBase darklord = darklordMonster.Categories.Single(category => category.MetadataKey == "archetype:darklord");
        Card manualDarklord = new Card([], externalCardId: 90000003).WithManualMetadataCategory(darklord);
        Assert.That(ixchelCost.Matches(manualDarklord), Is.False,
            "A manually added Darklord property cannot pay Ixchel's objective hand cost.");

        List<CategoryBase> tuneProperties = [
            new("Monster", CategorySource.Metadata, "kind:monster"),
            new("Level 4", CategorySource.Metadata, "level:4"),
            new("Insect", CategorySource.Metadata, "monster-race:insect")
        ];
        Card manualTuneMaterial = new Card([], externalCardId: 90000004);
        foreach (CategoryBase property in tuneProperties) {
            manualTuneMaterial = manualTuneMaterial.WithManualMetadataCategory(property);
        }
        Assert.That(tuneCost.Matches(manualTuneMaterial), Is.False,
            "Manual monster, race and level labels cannot pay Tune's objective hand cost.");
        Assert.That(ixchel.PreDrawRemovalCount, Is.EqualTo(1), "Ixchel's activating source is consumed separately from its one other card cost.");
        Assert.That(tune.PreDrawRemovalCount, Is.EqualTo(1), "Tune discards one card chosen from its OR of legal cost clauses.");
    }

    [Test]
    public async Task ImportSelectionRestoreAndSessionRoundTripPreserveEachBuiltInPlan() {
        SessionService codec = new(Mock.Of<IJSRuntime>(), new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());
        foreach (MaintainedCardEffect effect in Registry.All) {
            CardInfo info = CatalogInfo(effect.Name);
            Card imported = Registry.CreateImportedCard(info, 2, info.Id);
            Card previous = new([], id: "previous", externalCardId: 99999999);
            Card replacement = new(CardPropertyProvider.GetCategories(info), 2, info.Name, id: "stable", externalCardId: info.Id);
            Card selected = Registry.ApplyForExplicitSelection(replacement, previous, info);
            Card modified = effect.Plan.Search is not null
                ? imported.WithOncePerTurn(!effect.Plan.OncePerTurn)
                : imported.WithPreDrawSelector(CardSelector.Any);
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
    public void EngageSearchMatchesSmallDeckProbability(CalculationModel mode) {
        Card source = Imported("Sky Striker Mobilize - Engage!");
        Card target = Objective("Sky Striker Ace - Raye");
        List<Card> deck = [source, target, new([], 3, "Filler", id: "filler")];
        List<Combo> combos = [new([], "Access Raye", cards: [new(target.Id, 1, 1)])];

        ProbabilityCalculationResult result = Calculate(deck, combos, 1, mode);

        AssertProbability(result, 2, 5);
    }

    [TestCase(CalculationModel.Fast)]
    [TestCase(CalculationModel.Exact)]
    public void IxchelSourceAndOneOtherDarklordCostMatchSmallDeckProbability(CalculationModel mode) {
        Card source = Imported("Darklord Ixchel");
        Card cost = Objective("Darklord Nasten");
        List<Card> deck = [source, cost, new([Goal], id: "goal"), new([], 2, id: "filler")];
        List<Combo> combos = [new([new(Goal, 1, 2)], "See goal")];

        ProbabilityCalculationResult result = Calculate(deck, combos, 2, mode);

        AssertProbability(result, 7, 15);
    }

    [TestCase(CalculationModel.Fast)]
    [TestCase(CalculationModel.Exact)]
    public void TuneOrOfConjunctionCostsMatchSmallDeckProbability(CalculationModel mode) {
        Card source = Imported("Traptantalizing Tune");
        List<Card> deck = [
            source,
            Objective("Resonance Insect"),
            Objective("Angel Trumpeter"),
            Objective("Reckless Greed"),
            new([Goal], id: "goal"),
            new([], id: "filler")
        ];
        List<Combo> combos = [new([new(Goal, 1, 2)], "See goal")];

        ProbabilityCalculationResult result = Calculate(deck, combos, 2, mode);

        AssertProbability(result, 13, 30);
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

    private static CardInfo CatalogInfo(string name) {
        string catalogPath = FindCatalogPath();
        using JsonDocument catalog = JsonDocument.Parse(File.ReadAllText(catalogPath));
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
