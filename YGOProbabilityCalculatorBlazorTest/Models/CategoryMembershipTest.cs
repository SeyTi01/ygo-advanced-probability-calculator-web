using System.Text.Json;
using Microsoft.JSInterop;
using Moq;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Converter;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.Session;

namespace YGOProbabilityCalculatorBlazorTest.Models;

[TestFixture]
public class CategoryMembershipTest {
    private static readonly CategoryBase Monster = new("Monster", CategorySource.Metadata, "kind:monster");
    private static readonly CategoryBase Vs = new("Archetype: Vanquish Soul", CategorySource.Metadata, "archetype:vanquish%20soul");
    private static readonly CategoryBase Orcust = new("Archetype: Orcust", CategorySource.Metadata, "archetype:orcust");
    private static readonly CategoryBase Fire = new("Attribute: FIRE", CategorySource.Metadata, "attribute:fire");
    private static CardSelector VsRule => CardSelector.AllOf([Vs.Identity, Monster.Identity]);
    private static CategoryBase Role => new("VS Monster", rule: VsRule);

    [Test]
    public void DefinitionIsMetadataOnlyBoundedAndIdentityIsUnchanged() {
        Assert.Multiple(() => {
            Assert.Throws<ArgumentException>(() => new CategoryBase("Role", rule: CardSelector.Any));
            Assert.Throws<ArgumentException>(() => new CategoryBase("Role", rule: CardSelector.For(new("Other"))));
            Assert.Throws<ArgumentException>(() => new CategoryBase("Metadata", CategorySource.Metadata, "key", VsRule));
            Assert.Throws<ArgumentException>(() => new CategoryBase("Role", rule: CardSelector.AnyOf(Enumerable.Range(0, 17).Select(index => $"metadata:{index}"))));
            Assert.Throws<ArgumentException>(() => new CategoryBase("Role", rule: CardSelector.AllOf(Enumerable.Range(0, 17).Select(index => $"metadata:{index}"))));
            Assert.That(Role, Is.EqualTo(new CategoryBase("VS Monster")));
            Assert.That(Role, Is.Not.EqualTo(new CategoryBase("VS Monster", CategorySource.Metadata, "vs")));
            Assert.That(Role, Is.Not.EqualTo(new CategoryBase("Different")));
        });
        CardSelector repeated = CardSelector.AnyOfSelectors([VsRule, CardSelector.AllOf([Monster.Identity, Vs.Identity, Vs.Identity])]);
        Assert.That(repeated, Is.EqualTo(VsRule));
    }

    [Test]
    public void MetadataLifecycleKeepsManualExceptionsAndNeverMutatesAssignments() {
        Card automatic = new([Vs, Monster], 3, "Razen");
        Card manual = new([Role], 1, "ROTA");
        Card both = new([Vs, Monster, Role]);
        Assert.Multiple(() => {
            Assert.That(CategoryMembership.IsAutomatic(automatic, Role), Is.True);
            Assert.That(CategoryMembership.IsAutomatic(manual, Role), Is.False);
            Assert.That(CategoryMembership.IsAutomatic(both, Role), Is.True);
            Assert.That(CategoryMembership.EffectiveCategories(both, [Role]).Count(category => category.Equals(Role)), Is.EqualTo(1));
            Assert.That(CategoryMembership.EffectiveCategories(new Card([Vs]), [Role]), Does.Not.Contain(Role));
            Assert.That(CategoryMembership.EffectiveCategories(new Card([]), [Role]), Is.Empty);
            Assert.That(CategoryMembership.Materialize([automatic], [Role])[0].Categories, Does.Contain(Role));
            Assert.That(automatic.Categories, Does.Not.Contain(Role));
            Assert.That(CategoryMembership.EffectiveCategories(automatic.Duplicate().WithActive(false), [Role]), Does.Contain(Role));
            Assert.That(CategoryMembership.EffectiveCategories(automatic.WithObjectiveMetadata([Monster], 1), [Role]), Does.Not.Contain(Role));
            Assert.That(CategoryMembership.EffectiveCategories(manual.WithObjectiveMetadata([Monster], 2), [Role]), Does.Contain(Role));
            Assert.That(CategoryMembership.EffectiveCategories(automatic, []), Does.Not.Contain(Role));
            Assert.That(CategoryMembership.EffectiveCategories(manual, []), Does.Contain(Role));
        });
        Card overridden = new Card([Vs]).WithManualMetadataCategory(Monster);
        Assert.That(CategoryMembership.EffectiveCategories(overridden, [Role]), Does.Contain(Role));
        Assert.That(CategoryMembership.EffectiveCategories(overridden.WithoutManualMetadataCategory(Monster.MetadataKey!), [Role]), Does.Not.Contain(Role));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void ExactWorkerResultsEqualIndependentlyAssignedDeck(int removal) {
        bool draw = removal > 0;
        CategoryBase role = new("Role", rule: CardSelector.AnyOfSelectors([VsRule, CardSelector.AllOf([Orcust.Identity, Monster.Identity])]));
        CategoryBase manualRole = new("Role");
        Card[] ruleDeck = [
            new([Vs, Monster, Fire], 2, "VS", id: "vs"),
            new([Orcust, Monster], 2, "Orcust", id: "orcust"),
            new([manualRole, Fire], 1, "Surrogate", id: "surrogate"),
            new([Vs], 1, "Spell", id: "spell"),
            new([], 2, "Other", id: "other"),
            new([], 1, "Draw", id: "draw", drawCount: draw ? 1 : null,
                postDrawDiscardCount: removal == 1 ? 1 : 0, postDrawSelector: removal == 1 ? CardSelector.For(role) : CardSelector.Any,
                preDrawRemovalCount: removal == 2 ? 1 : 0, preDrawSelector: removal == 2 ? CardSelector.For(role) : CardSelector.Any)
        ];
        // Explicitly enumerate expected memberships, without using the rule matcher.
        Card[] manualDeck = [
            ruleDeck[0].WithCategories([Vs, Monster, Fire, manualRole]),
            ruleDeck[1].WithCategories([Orcust, Monster, manualRole]),
            ruleDeck[2], ruleDeck[3], ruleDeck[4], ruleDeck[5]
        ];
        Combo[] combos = [
            new([new(role, 1, 2), new(Fire, 1, 3)], "Distinct roles", groupId: "group"),
            new([ComboCategory.SameCard([role, Fire], 1, 3)], "Same card", groupId: "group"),
            new([new(role, 0, 0)], "No role"),
            new([], "OR", alternativeGroups: [new ComboAlternativeGroup([
                ComboAlternative.For(new ComboCategory(role, 2, 3)),
                ComboAlternative.For(new ComboCategory(Fire, 1, 1))])])
        ];
        ComboGroup[] groups = [new("group", "Routes")];
        CalculationSnapshot snapshot = CalculationSnapshot.Capture(ruleDeck, combos, 3, groups, categoryDefinitions: [role]);
        ProbabilityCalculationResult actual = CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json));
        ProbabilityCalculationResult expected = new ProbabilityCalculatorService().CalculateProbabilityResults(manualDeck.ToList(), combos.ToList(), 3, groups);
        Assert.Multiple(() => {
            Assert.That(actual.TotalProbability, Is.EqualTo(expected.TotalProbability));
            Assert.That(actual.ComboProbabilities, Is.EqualTo(expected.ComboProbabilities));
            Assert.That(actual.GroupProbabilities, Is.EqualTo(expected.GroupProbabilities));
            Assert.That(ruleDeck[0].Categories, Does.Not.Contain(role));
        });
    }

    [Test]
    public void FortyCardCalculationUsesTheSameFlatWorkerInputAsManualAssignment() {
        CategoryBase manualRole = new(Role.Name);
        Card[] source = Enumerable.Range(0, 14).Select(index => new Card(
            index < 5 ? [Vs, Monster] : [], index == 13 ? 1 : 3, id: $"card-{index}")).ToArray();
        Card[] manual = source.Select((card, index) => index < 5 ? card.WithCategories([Vs, Monster, manualRole]) : card).ToArray();
        Combo[] combos = [new([new(manualRole, 1, 5)])];
        CalculationSnapshot expected = CalculationSnapshot.Capture(manual, combos, 5, []);
        CalculationSnapshot actual = CalculationSnapshot.Capture(source, combos, 5, [], categoryDefinitions: [Role]);
        Assert.That(actual.Json, Is.EqualTo(expected.Json), "Rules never add dimensions or definitions to calculation states.");
        CalculationWire.Execute(actual.Json);
        System.Diagnostics.Stopwatch timer = System.Diagnostics.Stopwatch.StartNew();
        for (int repeat = 0; repeat < 20; repeat++) {
            CalculationWire.Execute(CalculationSnapshot.Capture(source, combos, 5, [], categoryDefinitions: [Role]).Json);
        }
        double withRules = timer.Elapsed.TotalMilliseconds / 20;
        timer.Restart();
        for (int repeat = 0; repeat < 20; repeat++) {
            CalculationWire.Execute(CalculationSnapshot.Capture(manual, combos, 5, []).Json);
        }
        TestContext.Out.WriteLine($"40 cards / 5 hand: rules {withRules:F3} ms, manual {timer.Elapsed.TotalMilliseconds / 20:F3} ms per capture + calculation.");
    }

    [Test]
    public async Task SessionRoundTripKeepsDefinitionsSeparateAndRebuildsAfterReload() {
        SessionService codec = new(Mock.Of<IJSRuntime>(), new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());
        SessionState session = new() {
            Categories = [Role], Cards = [new([Vs, Monster], 3, "Razen", id: "razen"), new([Role], name: "ROTA", id: "rota")],
            Combos = [new([new(Role, 1, 5)], cards: [new("rota", 1, 5)])], HandSize = 5,
            CategoryColorIndices = new() { [Role.Name] = 2 }
        };
        string json = codec.SerializeSession(session);
        SessionState loaded = await codec.LoadSessionAsync(json);
        Assert.Multiple(() => {
            Assert.That(loaded.SchemaVersion, Is.EqualTo(5));
            Assert.That(loaded.Categories.Single().Rule, Is.EqualTo(VsRule));
            Assert.That(loaded.Cards[0].Categories, Does.Not.Contain(Role));
            Assert.That(loaded.Cards[1].Categories, Does.Contain(Role));
            Assert.That(CategoryMembership.Materialize(loaded.Cards, loaded.Categories).All(card => card.Categories.Contains(Role)), Is.True);
            Assert.That(loaded.Combos[0].Cards[0].CardId, Is.EqualTo("rota"));
            Assert.That(loaded.CategoryColorIndices[Role.Name], Is.EqualTo(2));
            Assert.That(json.Split("\"Rule\"").Length - 1, Is.EqualTo(1));
            Assert.That(codec.SerializeSession(loaded), Is.EqualTo(json));
        });
    }

    [Test]
    public async Task ReleasedSessionRemainsManualAndIgnoresUnknownRuleExtension() {
        SessionService codec = new(Mock.Of<IJSRuntime>(), new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());
        string legacy = await File.ReadAllTextAsync(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "legacy_v1_2_example_session.json"));
        SessionState loaded = await codec.LoadSessionAsync(legacy);
        Assert.That(loaded.Categories, Is.Not.Empty);
        Assert.That(loaded.Categories.All(category => category.Rule is null), Is.True);
        SessionState schema3 = await codec.LoadSessionAsync("""
            {"SchemaVersion":3,"Categories":[{"Name":"Role","Rule":{"Kind":"Any","Categories":[]}}],"Cards":[],"Combos":[],"HandSize":5}
            """);
        Assert.That(schema3.Categories.Single().Rule, Is.Null);
    }

    [TestCase("{\"Kind\":\"Any\",\"Categories\":[]}")]
    [TestCase("{\"Kind\":\"Category\",\"Categories\":[\"user:Other\"]}")]
    [TestCase("null")]
    public void InvalidPersistedRuleIsRejected(string rule) {
        JsonSerializerOptions options = new();
        options.Converters.Add(new CategoryBaseConverter());
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CategoryBase>("{\"Name\":\"Role\",\"Rule\":" + rule + "}", options));
    }
}
