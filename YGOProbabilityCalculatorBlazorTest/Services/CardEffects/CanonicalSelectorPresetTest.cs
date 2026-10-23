using System.Text.Json;
using Microsoft.JSInterop;
using Moq;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;
using YGOProbabilityCalculatorBlazor.Services.DeckImport;
using YGOProbabilityCalculatorBlazor.Services.Session;

namespace YGOProbabilityCalculatorBlazorTest.Services.CardEffects;

public sealed class CanonicalSelectorPresetTest {
    private static readonly (int Id, int Minimum, int Maximum, string[] Facets)[] NumericPresets = [
        (32807846, 1, 4, ["monster-race:warrior"]),
        (85106525, 1, 4, ["monster-race:pyro"]),
        (57103969, 1, 4, ["monster-race:beast-warrior"]),
        (72323266, 1, 3, ["monster-trait:tuner", "monster-race:fiend"]),
        (96729612, 1, 7, ["monster-type:ritual"]),
        (67115133, 1, 4, ["archetype:radiant%20typhoon"]),
        (38120068, 8, 8, []),
        (48805472, 4, 4, ["monster-race:plant"])
    ];

    private static CategoryBase Property(string key) => new(key, CategorySource.Metadata, key);
    private static Card Target(IEnumerable<string> keys) => new(keys.Select(Property), externalCardId: 100);
    private static IEnumerable<CardSelector> Nodes(CardSelector selector) => new[] { selector }.Concat(selector.Children.SelectMany(Nodes));
    private static CardSelector Selector(DrawEffectPlan plan) => plan.Search?.Selector ?? plan.HandCostSelector;
    private static bool Matches(DrawEffectPlan plan, Card card) => plan.Search?.Matches(card) ?? plan.HandCostSelector.Matches(card);
    private static SessionService Codec() => new(Mock.Of<IJSRuntime>(), new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());

    [Test]
    public void NewRevisionsUseOneRealNumericPredicateWithoutLiteralLevelBranches() {
        foreach ((int id, int minimum, int maximum, string[] _) in NumericPresets) {
            MaintainedCardEffect current = CardEffectRegistry.Default.Find(id)!;
            CardSelector selector = Selector(current.Plan);
            CardSelector numeric = Nodes(selector).Single(node => node.Kind == CardSelectorKind.NumericRange);
            Assert.That(numeric.NumericRange, Is.EqualTo(new NumericPropertyRange(NumericPropertyKind.Level, minimum, maximum)), current.Name);
            Assert.That(selector.CategoryIdentities.Any(identity => identity.StartsWith("metadata:level:", StringComparison.Ordinal)), Is.False, current.Name);
            Assert.That(selector.CategoryIdentities, Does.Not.Contain("metadata:kind:monster"), current.Name);
            Assert.That(current.Profile.Revision, Is.EqualTo(LegacySelectorPlans.Revision(id) + 1));
            if (id == 67115133) {
                Assert.That(current.Plan.Search!.Modes!.Select(mode => mode.ActivationLimit), Is.EqualTo(new[] { 1, 1 }));
                Assert.That(current.Plan.Search.Modes[0].Selector, Is.EqualTo(selector.AuthoredTerms.First()));
                Assert.That(current.Plan.Search.Modes[1].Selector, Is.EqualTo(CardSelector.CatalogIdentity(5318639)));
            }
        }
        CardSelector openEnded = CardEffectRegistry.Default.Find(92221402)!.Plan.Search!.Selector;
        Assert.That(Nodes(openEnded).Single(node => node.Kind == CardSelectorKind.LevelAtLeast).MinimumLevel, Is.EqualTo(5));
        Assert.That(openEnded.Matches(Target(["kind:monster", "level:100", "attribute:dark", "monster-race:aqua"])), Is.True);
    }

    [Test]
    public async Task EveryChangedSearchPaymentAndModeKeepsTheCatalogTargetSet() {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "YGOProbabilityCalculatorBlazor/wwwroot/data/card-catalog.v1.json"))) {
            directory = directory.Parent;
        }
        string path = Path.Combine(directory!.FullName, "YGOProbabilityCalculatorBlazor/wwwroot/data/card-catalog.v1.json");
        using JsonDocument catalog = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        Card[] cards = catalog.RootElement.GetProperty("cards").EnumerateArray()
            .Select(element => element.Deserialize<CardInfo>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!)
            .Select(info => new Card(CardPropertyProvider.GetCategories(info), externalCardId: info.Id)).ToArray();
        Assert.That(cards.Length, Is.GreaterThan(1000));
        foreach (int id in LegacySelectorPlans.Ids) {
            DrawEffectPlan previous = LegacySelectorPlans.Plan(id);
            DrawEffectPlan current = CardEffectRegistry.Default.Find(id)!.Plan;
            Assert.That(cards.Where(card => Matches(current, card)).Select(card => card.ExternalCardId),
                Is.EqualTo(cards.Where(card => Matches(previous, card)).Select(card => card.ExternalCardId)), $"{id} catalog eligibility");
            for (int mode = 0; mode < (previous.Search?.Modes?.Count ?? 0); mode++) {
                Assert.That(cards.Where(card => current.Search!.Matches(card, current.Search.Modes![mode].Selector)).Select(card => card.ExternalCardId),
                    Is.EqualTo(cards.Where(card => previous.Search!.Matches(card, previous.Search.Modes![mode].Selector)).Select(card => card.ExternalCardId)), $"{id} mode {mode}");
            }
            Assert.That(current with { HandCostSelector = previous.HandCostSelector, Search = previous.Search }, Is.EqualTo(previous),
                $"{id}: all activation, draw, removal and mode-independent settings remain unchanged");
        }
    }

    [Test]
    public void CanonicalRangesValidateBoundsDimensionAndMetadataWhileSavedLiteralsRemainLiteral() {
        foreach ((int id, int minimum, int maximum, string[] facets) in NumericPresets) {
            DrawEffectPlan current = CardEffectRegistry.Default.Find(id)!.Plan;
            DrawEffectPlan previous = LegacySelectorPlans.Plan(id);
            for (int level = 0; level <= maximum + 1; level++) {
                Card valid = Target(["kind:monster", .. facets, $"level:{level}"]);
                Assert.That(Matches(current, valid), Is.EqualTo(level >= minimum && level <= maximum), $"{id} Level {level}");
                Assert.That(Matches(current, valid), Is.EqualTo(Matches(previous, valid)));
            }
            Card inside = Target(["kind:monster", .. facets, $"level:{maximum}"]);
            foreach (string contradiction in new[] { "rank:4", "link:1", "monster-type:xyz", "monster-type:link", $"level:{maximum + 1}" }) {
                Card contradictory = Target(inside.Categories.Select(category => category.MetadataKey!).Append(contradiction));
                Assert.That(Matches(current, contradictory), Is.False, $"{id} rejects {contradiction}");
                Assert.That(Matches(previous, contradictory), Is.True, $"{id} saved literal remains permissive");
            }
            Card traitOnly = Target([.. facets, $"level:{maximum}"]);
            Assert.That(Matches(current, traitOnly), Is.False, $"{id} canonical numeric requires Monster metadata");
            Assert.That(Matches(previous, traitOnly), Is.True, $"{id} historical trait-only metadata remains valid");
            Assert.That(Matches(current, Target(["kind:monster", .. facets, "rank:4"])), Is.False, $"{id} Rank cannot stand for Level");
            Assert.That(Matches(current, Target(["kind:monster", .. facets, "link:1"])), Is.False, $"{id} Link cannot stand for Level");
            Card manualLevel = Target(["kind:monster", .. facets]).WithManualMetadataCategory(Property($"level:{maximum}"));
            Assert.That(Matches(current, manualLevel), Is.EqualTo(id == 38120068), $"{id} keeps objective-versus-manual provenance");
            foreach (string facet in facets) {
                Card manualFacet = Target(["kind:monster", .. facets.Where(key => key != facet), $"level:{maximum}"])
                    .WithManualMetadataCategory(Property(facet));
                Assert.That(Matches(current, manualFacet), Is.False, $"{id} cannot synthesize an objective target/payment facet");
            }
            Assert.That(Matches(current, new Card(inside.Categories)), Is.EqualTo(id == 38120068), $"{id} external identity policy");
        }
        Card manualOnlyLevel = new([Property("level:8")]);
        Assert.That(LegacySelectorPlans.Plan(38120068).HandCostSelector.Matches(manualOnlyLevel), Is.True);
        Assert.That(CardEffectRegistry.Default.Find(38120068)!.Plan.HandCostSelector.Matches(manualOnlyLevel), Is.False);
        Card subtypeOnly = Target(["trap-type:normal"]);
        Assert.That(LegacySelectorPlans.Plan(48805472).HandCostSelector.Matches(subtypeOnly), Is.False);
        Assert.That(CardEffectRegistry.Default.Find(48805472)!.Plan.HandCostSelector.Matches(subtypeOnly), Is.True,
            "The new revision treats the objective Normal Trap subtype as sufficient; saved snapshots still require Trap.");
        Assert.That(CardEffectRegistry.Default.Find(48805472)!.Plan.HandCostSelector.Matches(new Card([],
            externalCardId: 100).WithManualMetadataCategory(Property("trap-type:normal"))), Is.False);
    }

    [Test]
    public void CommonFactorsPreserveAllSyntheticSubsetsIncludingIncompleteAndManualMetadata() {
        foreach ((int id, string[] keys) in new (int, string[])[] {
            (26845680, ["kind:monster", "kind:spell", "archetype:orcust", "archetype:world%20legacy"]),
            (60764609, ["archetype:fiendsmith", "kind:spell", "kind:trap", "kind:monster"])
        }) {
            DrawEffectPlan previous = LegacySelectorPlans.Plan(id);
            DrawEffectPlan current = CardEffectRegistry.Default.Find(id)!.Plan;
            for (int mask = 0; mask < 1 << keys.Length; mask++) {
                Card card = Target(keys.Where((_, index) => (mask & 1 << index) != 0));
                foreach (Card candidate in new[] { card, new Card(card.Categories), card.WithActive(false), card.WithCopies(0),
                    new Card([], externalCardId: 100).WithManualMetadataCategory(Property(keys[0])) }) {
                    Assert.That(Matches(current, candidate), Is.EqualTo(Matches(previous, candidate)), $"{id} subset {mask}");
                }
            }
            Assert.That(Selector(current).Kind, Is.EqualTo(CardSelectorKind.AllOf));
            Assert.That(Selector(current).AuthoredTerms.Select(term => term.Kind), Is.EqualTo(new[] { CardSelectorKind.Category, CardSelectorKind.AnyOf }));
        }
    }

    [Test]
    public async Task OldBuiltInAndCustomPlansSurviveSaveShareWorkerAndLoadUntilExplicitRestore() {
        foreach (int id in LegacySelectorPlans.Ids) {
            MaintainedCardEffect maintained = CardEffectRegistry.Default.Find(id)!;
            DrawEffectPlan legacy = LegacySelectorPlans.Plan(id);
            foreach (EffectSource source in new[] { EffectSource.BuiltIn, EffectSource.Custom }) {
                EffectProfile profile = source == EffectSource.BuiltIn
                    ? maintained.Profile with { Revision = LegacySelectorPlans.Revision(id) } : maintained.Profile.AsCustom();
                Card saved = new Card([], externalCardId: id).WithEffect(legacy, profile);
                string json = Codec().SerializeSession(new() { Cards = [saved], HandSize = 1 });
                string fragment = new Uri(SessionShareCodec.CreateLink("https://example.invalid/", json)).Fragment;
                SessionState loaded = await Codec().LoadSessionAsync(SessionShareCodec.Decode(fragment));
                Card retained = loaded.Cards.Single();
                Assert.That(retained.EffectPlan, Is.EqualTo(legacy), $"{id} {source} saved predicate");
                Assert.That(retained.EffectProfile, Is.EqualTo(profile));
                Assert.That(Codec().SerializeSession(loaded), Is.EqualTo(json), "Save never upgrades a plan.");
                CalculationSnapshot snapshot = CalculationSnapshot.Capture(loaded.Cards, [], 1, []);
                CalculationInput worker = JsonSerializer.Deserialize(snapshot.Json, CalculationJsonContext.Default.CalculationInput)!;
                Assert.That(worker.Cards.Single().Search, Is.EqualTo(legacy.Search));
                Assert.That(worker.Cards.Single().PreDrawSelector ?? CardSelector.Any, Is.EqualTo(legacy.HandCostSelector));
                Card restored = CardEffectRegistry.Default.RestoreBuiltIn(retained);
                Card imported = CardEffectRegistry.Default.CreateImportedCard(new() { Id = id, Name = "Any display label" }, 1, id);
                Assert.That(restored.EffectPlan, Is.EqualTo(maintained.Plan));
                Assert.That(imported.EffectPlan, Is.EqualTo(maintained.Plan));
                Assert.That(restored.EffectProfile.Revision, Is.EqualTo(LegacySelectorPlans.Revision(id) + 1));
                Assert.That(imported.EffectProfile.Revision, Is.EqualTo(restored.EffectProfile.Revision));
            }
        }
    }

    [Test]
    public async Task GappedCustomLevelsRemainAnAuthoredOrRatherThanAnInclusiveRange() {
        CardSelector gapped = CardSelector.AllOfSelectors([
            CardSelector.For(Property("monster-race:warrior")),
            CardSelector.AnyOf(["metadata:level:1", "metadata:level:3", "metadata:level:4"])
        ]);
        Card source = new Card([]).WithSearch(new(gapped, false));
        SessionState loaded = await Codec().LoadSessionAsync(Codec().SerializeSession(new() { Cards = [source] }));
        CardSelector retained = loaded.Cards.Single().Search!.Selector;
        Assert.That(retained.HasNumericPredicate, Is.False);
        foreach (int level in new[] { 1, 2, 3, 4 }) {
            Assert.That(retained.Matches(Target(["kind:monster", "monster-race:warrior", $"level:{level}"])), Is.EqualTo(level != 2));
        }
        Assert.That(retained.AuthoredTerms.Last().AuthoredTerms.Select(term => term.CategoryIdentities.Single()),
            Is.EqualTo(new[] { "metadata:level:1", "metadata:level:3", "metadata:level:4" }));
    }
}
