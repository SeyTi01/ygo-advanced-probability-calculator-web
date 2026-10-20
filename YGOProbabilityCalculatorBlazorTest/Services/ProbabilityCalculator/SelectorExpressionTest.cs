using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

public sealed class SelectorExpressionTest {
    internal static CardSelector Compact => new ChaoticElementsEffect().Compile().Search!.Selector;
    internal static CardSelector Old => CardSelector.AnyOfSelectors(
        from level in Enumerable.Range(5, 9)
        from attribute in new[] { "light", "dark" }
        from race in new[] { "pyro", "aqua" }
        select CardSelector.AllOf(["metadata:kind:monster", $"metadata:level:{level}",
            $"metadata:attribute:{attribute}", $"metadata:monster-race:{race}"]));

    internal static Card Target(int level = 5, string attribute = "dark", string race = "aqua", string kind = "monster") => new([
        new(kind, CategorySource.Metadata, $"kind:{kind}"), new("Level", CategorySource.Metadata, $"level:{level}"),
        new(attribute, CategorySource.Metadata, $"attribute:{attribute}"), new(race, CategorySource.Metadata, $"monster-race:{race}")
    ], name: $"Level {level} {attribute} {race}", externalCardId: 100, id: $"target-{level}-{attribute}-{race}-{kind}");

    [Test]
    public void ExhaustivePrintedRuleAndOldDomainAgreeWithoutCappingNumericLevel() {
        SearchEffectPlan search = new(Compact);
        foreach (int level in Enumerable.Range(0, 21).Append(int.MaxValue)) {
            foreach (string attribute in new[] { "light", "dark", "earth" }) {
                foreach (string race in new[] { "pyro", "aqua", "dragon" }) {
                    foreach (string kind in new[] { "monster", "spell" }) {
                        Card target = Target(level, attribute, race, kind);
                        bool expected = level >= 5 && kind == "monster" && attribute != "earth" && race != "dragon";
                        Assert.That(search.Matches(target), Is.EqualTo(expected), target.Name + kind);
                        if (level <= 13) {
                            Assert.That(Compact.Matches(target), Is.EqualTo(Old.Matches(target)), target.Name + kind);
                        }
                        Assert.That(search.Matches(target.WithCopies(0)), Is.False);
                        Assert.That(search.Matches(target.WithActive(false)), Is.False);
                    }
                }
            }
        }
        Assert.That(Old.Matches(Target(14)), Is.False);
    }

    [Test]
    public void NumericPredicateUsesObjectiveLevelEvenForCustomSearchAndRemoval() {
        Card target = Target();
        List<Card> invalid = [new(target.Categories), new([], externalCardId: 100),
            target.WithCategories(target.Categories.Where(category => category.MetadataKey != "level:5")),
            target.WithCategories(target.Categories.Where(category => category.MetadataKey != "level:5")
                .Append(new("Rank 5", CategorySource.Metadata, "rank:5"))),
            target.WithCategories(target.Categories.Append(new("Link", CategorySource.Metadata, "monster-type:link"))),
            target.WithCategories(target.Categories.Append(new("Rank", CategorySource.Metadata, "rank:5"))),
            target.WithCategories(target.Categories.Append(new("Unknown Level", CategorySource.Metadata, "level:?"))),
            target.WithCategories(target.Categories.Where(category => category.MetadataKey != "level:5"))
                .WithManualMetadataCategory(new("Level 14", CategorySource.Metadata, "level:14")),
            new([new("Level 5"), new("Monster")], externalCardId: 100)];
        Card fakeSpell = new([new("Spell", CategorySource.Metadata, "kind:spell")], externalCardId: 101);
        foreach (CategoryBase category in target.Categories) {
            fakeSpell = fakeSpell.WithManualMetadataCategory(category);
        }
        invalid.Add(fakeSpell);
        foreach (Card card in invalid) {
            Assert.That(new SearchEffectPlan(Compact, false).Matches(card), Is.False, card.Name);
            Assert.That(CardSelectorCompiler.Compile(Compact, [card], new(CalculationWorkPolicy.Interactive)), Is.EqualTo(new[] { false }));
        }
        Assert.That(CardSelector.LevelAtLeast(0).Matches(Target(0)), Is.True);
        Assert.That(CardSelector.LevelAtLeast(int.MaxValue).Matches(Target(int.MaxValue)), Is.True);
        Assert.Throws<ArgumentOutOfRangeException>(() => CardSelector.LevelAtLeast(-1));
    }

    [Test]
    public void GroupingNormalizationRenameAndRoundTripKeepBooleanMeaning() {
        CardSelector a = new(CardSelectorKind.Category, ["user:A"]);
        CardSelector b = new(CardSelectorKind.Category, ["user:B"]);
        CardSelector c = new(CardSelectorKind.Category, ["user:C"]);
        CardSelector grouped = CardSelector.AllOfSelectors([a, CardSelector.AnyOfSelectors([b, c])]);
        CardSelector different = CardSelector.AnyOfSelectors([CardSelector.AllOfSelectors([a, b]), c]);
        for (int mask = 0; mask < 8; mask++) {
            CategoryBase[] categories = new[] { new CategoryBase("A"), new("B"), new("C") }
                .Where((category, index) => (mask & (1 << index)) != 0).ToArray();
            Assert.That(grouped.Matches(categories), Is.EqualTo((mask & 1) != 0 && (mask & 6) != 0));
            Assert.That(different.Matches(categories), Is.EqualTo((mask & 3) == 3 || (mask & 4) != 0));
        }
        CardSelector duplicate = CardSelector.AllOfSelectors([CardSelector.AnyOfSelectors([c, b, b]), a, a]);
        Assert.That(duplicate.Signature, Is.EqualTo(grouped.Signature));
        Assert.That(duplicate.GetHashCode(), Is.EqualTo(grouped.GetHashCode()));
        CardSelector renamed = grouped.RenameCategory("user:C", "user:Renamed");
        Assert.That(renamed.Matches([new CategoryBase("A"), new("Renamed")]), Is.True);
        Assert.That(renamed.CategoryIdentities, Does.Not.Contain("user:C"));
        Assert.That(Compact.RenameCategory("user:Absent", "user:Other"), Is.EqualTo(Compact));
        foreach (CardSelector selector in new[] { grouped, Compact, Old, CardSelector.LevelAtLeast(int.MaxValue) }) {
            string json = JsonSerializer.Serialize(selector);
            Assert.That(JsonSerializer.Serialize(JsonSerializer.Deserialize<CardSelector>(json)), Is.EqualTo(json));
        }
        Assert.That(Compact.Children.Count, Is.EqualTo(3));
        Assert.That(Compact.CategoryIdentities.Count, Is.EqualTo(4));
    }

    [TestCase("{\"Kind\":\"LevelAtLeast\",\"Minimum\":-1}")]
    [TestCase("{\"Kind\":\"LevelAtLeast\",\"Minimum\":2147483648}")]
    [TestCase("{\"Kind\":\"LevelAtLeast\",\"Minimum\":5.1}")]
    [TestCase("{\"Kind\":\"LevelAtLeast\",\"Minimum\":\"5\"}")]
    [TestCase("{\"Kind\":\"LevelAtLeast\",\"Minimum\":null}")]
    [TestCase("{\"Kind\":\"LevelAtLeast\",\"Minimum\":5,\"Minimum\":6}")]
    [TestCase("{\"Kind\":\"LevelAtLeast\",\"Categories\":[\"metadata:level:5\"]}")]
    [TestCase("{\"Kind\":\"AllOf\",\"Children\":[{\"Kind\":\"Any\",\"Categories\":[]}]}")]
    public void MalformedNumericAndBooleanPayloadsFailClosed(string json) =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CardSelector>(json));

    [Test]
    public void RawAndConstructedSizeLimitsApplyBeforeDeduplicationAndWithoutExpansion() {
        string raw = "{\"Kind\":\"LevelAtLeast\",\"Minimum\":5}";
        for (int i = 0; i < CardSelector.MaximumDepth; i++) {
            raw = "{\"Kind\":\"AllOf\",\"Children\":[" + raw + "]}";
        }
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CardSelector>(raw));
        string duplicateNodes = "{\"Kind\":\"AnyOf\",\"Children\":[" + string.Join(",", Enumerable.Repeat(
            "{\"Kind\":\"LevelAtLeast\",\"Minimum\":5}", CardSelector.MaximumNodes)) + "]}";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CardSelector>(duplicateNodes));
        Assert.Throws<ArgumentException>(() => CardSelector.AllOf(Enumerable.Repeat("user:A", 2049)));
        CardSelector selector = Compact;
        for (int i = 0; i < 6; i++) {
            selector = i % 2 == 0 ? CardSelector.AnyOfSelectors([selector, CardSelector.LevelAtLeast(i)])
                : CardSelector.AllOfSelectors([selector, CardSelector.LevelAtLeast(i)]);
        }
        Assert.That(selector.Depth, Is.EqualTo(8));
        Assert.Throws<ArgumentException>(() => CardSelector.AnyOfSelectors([selector, CardSelector.LevelAtLeast(100)]));
    }
}
