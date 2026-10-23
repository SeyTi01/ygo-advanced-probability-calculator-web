using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;
using YGOProbabilityCalculatorBlazor.Services.DeckImport;

namespace YGOProbabilityCalculatorBlazorTest.Services.CardEffects;

public sealed class SelectorPolishEquivalenceTest {
    private static readonly (int Id, int Maximum, string[] Facets)[] Bounded = [
        (85106525, 4, ["kind:monster", "monster-race:pyro"]),
        (57103969, 4, ["kind:monster", "monster-race:beast-warrior"]),
        (72323266, 3, ["kind:monster", "monster-trait:tuner", "monster-race:fiend"]),
        (96729612, 7, ["kind:monster", "monster-type:ritual"]),
        (32807846, 4, ["kind:monster", "monster-race:warrior"])
    ];

    // Frozen pre-polish definitions, independent of the new factored construction.
    private static SearchEffectPlan Previous(int id) {
        if (id == 92221402) {
            return new(CardSelector.AllOfSelectors([
                CardSelector.AllOf(["metadata:kind:monster"]), CardSelector.LevelAtLeast(5),
                CardSelector.AnyOf(["metadata:attribute:light", "metadata:attribute:dark"]),
                CardSelector.AnyOf(["metadata:monster-race:pyro", "metadata:monster-race:aqua"])
            ]));
        }
        if (id == 98567237) {
            return new(CardSelector.AllOf(["metadata:attribute:light", "metadata:monster-race:fiend", "metadata:kind:monster"]));
        }
        if (id == 67115133) {
            CardSelector monster = CardSelector.AllOfSelectors([
                CardSelector.AllOf(["metadata:archetype:radiant%20typhoon", "metadata:kind:monster"]),
                CardSelector.AnyOf(["metadata:level:1", "metadata:level:2", "metadata:level:3", "metadata:level:4"])
            ]);
            return new(CardSelector.AnyOfSelectors([monster, CardSelector.CatalogIdentity(5318639)]));
        }
        (int _, int maximum, string[] facets) = Bounded.Single(entry => entry.Id == id);
        return new(CardSelector.AnyOfSelectors(Enumerable.Range(1, maximum).Select(level =>
            CardSelector.AllOf(facets.Select(key => "metadata:" + key).Append($"metadata:level:{level}")))));
    }

    private static CategoryBase Property(string key) => new(key, CategorySource.Metadata, key);

    [Test]
    public async Task AllChangedDefinitionsMatchPreviousPlansAcrossTheCheckedInCatalog() {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        string path = "";
        while (directory is not null) {
            path = Path.Combine(directory.FullName, "YGOProbabilityCalculatorBlazor", "wwwroot", "data", "card-catalog.v1.json");
            if (File.Exists(path)) { break; }
            directory = directory.Parent;
        }
        using JsonDocument catalog = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        Card[] cards = catalog.RootElement.GetProperty("cards").EnumerateArray()
            .Select(element => element.Deserialize<CardInfo>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!)
            .Select(info => new Card(CardPropertyProvider.GetCategories(info), externalCardId: info.Id)).ToArray();
        Assert.That(cards.Length, Is.GreaterThan(1000));
        foreach (int id in Bounded.Select(entry => entry.Id).Concat(new[] { 92221402, 98567237, 67115133 })) {
            SearchEffectPlan previous = Previous(id);
            SearchEffectPlan current = CardEffectRegistry.Default.Find(id)!.Plan.Search!;
            Assert.That(cards.Where(current.Matches).Select(card => card.ExternalCardId),
                Is.EqualTo(cards.Where(previous.Matches).Select(card => card.ExternalCardId)), $"Catalog target set for {id}");
        }
        CardSelector previousTune = CardSelector.Objective(CardSelector.AnyOfSelectors([
            CardSelector.AllOfSelectors([
                CardSelector.AllOf(["metadata:kind:monster", "metadata:level:4"]),
                CardSelector.AnyOf(["metadata:monster-race:insect", "metadata:monster-race:plant"])
            ]),
            CardSelector.AllOf(["metadata:kind:trap", "metadata:trap-type:normal"])
        ]));
        CardSelector currentTune = CardEffectRegistry.Default.Find(48805472)!.Plan.HandCostSelector;
        Assert.That(cards.Where(currentTune.Matches).Select(card => card.ExternalCardId),
            Is.EqualTo(cards.Where(previousTune.Matches).Select(card => card.ExternalCardId)), "Catalog payment set for Traptantalizing Tune");
    }

    [Test]
    public void ShippedLiteralPlansPreserveManualAndContradictoryNumbersWithMonsterKindPresent() {
        foreach (int id in Bounded.Select(entry => entry.Id).Append(92221402)) {
            SearchEffectPlan previous = Previous(id);
            SearchEffectPlan current = id == 92221402 ? CardEffectRegistry.Default.Find(id)!.Plan.Search! : LegacySelectorPlans.Plan(id).Search!;
            string[] facets = id == 92221402 ? ["kind:monster", "attribute:dark", "monster-race:aqua"] : Bounded.Single(entry => entry.Id == id).Facets;
            string[] keys = [.. facets, "level:1", "level:4", "level:5", "level:7", "level:8", "rank:4", "link:1", "monster-type:xyz", "kind:spell"];
            for (int mask = 0; mask < 1 << keys.Length; mask++) {
                CategoryBase[] properties = keys.Where((_, index) => (mask & 1 << index) != 0).Select(Property).ToArray();
                Card objective = new(properties.Append(Property("kind:monster")).Distinct().ToArray(), externalCardId: 100);
                foreach (Card card in new[] { objective, new Card(properties), objective.WithActive(false), objective.WithCopies(0),
                    objective.WithManualMetadataCategory(Property("level:3")) }) {
                    Assert.That(current.Matches(card), Is.EqualTo(previous.Matches(card)), $"{id}, metadata subset {mask}");
                }
            }
        }
    }

    [Test]
    public void LiteralRangesKeepTheirMeaningAndMonsterTraitsNeedNoSeparateKindCondition() {
        Card contradictory = new([Property("kind:monster"), Property("monster-race:warrior"), Property("level:4"), Property("rank:4")], externalCardId: 100);
        Assert.That(LegacySelectorPlans.Plan(32807846).Search!.Matches(contradictory), Is.True);
        Assert.That(CardEffectRegistry.Default.Find(32807846)!.Plan.Search!.Matches(contradictory), Is.False, "The new revision deliberately validates numeric metadata.");
        Assert.That(new NumericPropertyRange(NumericPropertyKind.Level, 1, 4).ToSelector().Matches(contradictory), Is.False);
        Card partial = new([Property("attribute:light"), Property("monster-race:fiend")], externalCardId: 100);
        Assert.That(CardEffectRegistry.Default.Find(98567237)!.Plan.Search!.Matches(partial), Is.True);
        Assert.That(CardSelector.AllOf(["metadata:attribute:light", "metadata:monster-race:fiend"]).Matches(partial), Is.True);
        foreach ((int id, string[] keys) in new (int, string[])[] {
            (32807846, ["monster-race:warrior", "level:4"]),
            (85106525, ["monster-race:pyro", "level:4"]),
            (57103969, ["monster-race:beast-warrior", "level:4"]),
            (72323266, ["monster-trait:tuner", "monster-race:fiend", "level:3"]),
            (96729612, ["monster-type:ritual", "level:7"]),
            (67115133, ["archetype:radiant%20typhoon", "level:4"]),
            (98567237, ["attribute:light", "monster-race:fiend"]),
            (48805472, ["level:4", "monster-race:plant"])
        }) {
            DrawEffectPlan plan = id == 98567237 ? CardEffectRegistry.Default.Find(id)!.Plan : LegacySelectorPlans.Plan(id);
            CardSelector selector = plan.Search?.Selector ?? plan.HandCostSelector;
            Assert.That(selector.CategoryIdentities, Does.Not.Contain("metadata:kind:monster"), $"{id} has no redundant Monster clause");
            Card traitOnly = new(keys.Select(Property).ToArray(), externalCardId: 100);
            Assert.That(plan.Search?.Matches(traitOnly) ?? selector.Matches(traitOnly), Is.True, $"{id} is determined by its monster-specific properties");
        }
        Card legacyPayment = new([Property("level:8")]);
        Assert.That(LegacySelectorPlans.Plan(38120068).HandCostSelector.Matches(legacyPayment), Is.True);
        Assert.That(CardEffectRegistry.Default.Find(38120068)!.Plan.HandCostSelector.Matches(legacyPayment), Is.False);
        Assert.That(new NumericPropertyRange(NumericPropertyKind.Level, 8, 8).ToSelector().Matches(legacyPayment), Is.False);
    }
}
