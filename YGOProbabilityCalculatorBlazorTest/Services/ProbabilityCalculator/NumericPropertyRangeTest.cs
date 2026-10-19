using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

public sealed class NumericPropertyRangeTest {
    private static Card Target(NumericPropertyKind property, int number) => new([
        new("Monster", CategorySource.Metadata, "kind:monster"),
        new(property.ToString(), CategorySource.Metadata, property.ToString().ToLowerInvariant() + ":" + number)
    ], id: property + "-" + number, externalCardId: 123);

    [TestCase(NumericPropertyKind.Level)]
    [TestCase(NumericPropertyKind.Rank)]
    [TestCase(NumericPropertyKind.Link)]
    public void InclusiveRangesEvaluateNumericMetadataAndKeepDimensionsSeparate(NumericPropertyKind property) {
        CardSelector selector = CardSelector.ForNumericRange(new(property, 3, 5));
        foreach (NumericPropertyKind actual in Enum.GetValues<NumericPropertyKind>()) {
            foreach (int number in Enumerable.Range(0, 9).Append(int.MaxValue)) {
                Assert.That(selector.Matches(Target(actual, number)), Is.EqualTo(actual == property && number >= 3 && number <= 5), $"{actual} {number}");
            }
        }
        Assert.That(CardSelector.ForNumericRange(new(property, 99, null)).Matches(Target(property, 100)), Is.True, "Predicates do not depend on catalog/deck values.");
        Assert.That(CardSelector.ForNumericRange(new(property, null, 0)).Matches(Target(property, 0)), Is.True);
        Assert.That(CardSelector.ForNumericRange(new(property, int.MaxValue, int.MaxValue)).Matches(Target(property, int.MaxValue)), Is.True);
    }

    [Test]
    public void LegacyObjectiveLevelPredicateAndNewRangeRoundTripKeepTheirProvenance() {
        foreach (NumericPropertyKind property in Enum.GetValues<NumericPropertyKind>()) {
            NumericPropertyRange range = new(property, 3, 5, true);
            CardSelector selector = range.ToSelector();
            Assert.That(JsonSerializer.Deserialize<CardSelector>(JsonSerializer.Serialize(selector)), Is.EqualTo(selector));
            ComboCategory requirement = ComboCategory.SameCard([range.ToCategory(), new("Role")], 0, 0);
            ComboCategory loaded = JsonSerializer.Deserialize<ComboCategory>(JsonSerializer.Serialize(requirement))!;
            Assert.That(loaded.Selector, Is.EqualTo(requirement.Selector));
            Assert.That(loaded.MinCount, Is.Zero);
            Assert.That(loaded.MaxCount, Is.Zero);
            Card original = Target(property, 4);
            Assert.That(selector.Matches(original), Is.True);
            Assert.That(selector.Matches(new Card(original.Categories)), Is.False);
            Assert.That(selector.Matches(original.WithCategories(original.Categories.Where(category => category.MetadataKey == "kind:monster"))
                .WithManualMetadataCategory(new("Manual", CategorySource.Metadata, range.Key + ":4"))), Is.False);
        }
        CardSelector legacy = CardSelector.LevelAtLeast(5);
        Assert.That(NumericPropertyRange.TryFromSelector(legacy, out NumericPropertyRange? old), Is.True);
        Assert.That(old!.ToSelector().Signature, Is.EqualTo(legacy.Signature));
        Assert.That(JsonSerializer.Serialize(old.ToSelector()), Is.EqualTo("{\"Kind\":\"LevelAtLeast\",\"Minimum\":5}"));
    }

    [TestCase(1, 2)]
    [TestCase(0, 0)]
    [TestCase(1, 1)]
    public void ComboNumericRangesAgreeWithIndependentPhysicalPairsAndDistinctCopies(int minimum, int maximum) {
        List<Card> deck = [.. Enumerable.Range(2, 5).Select(number => Target(NumericPropertyKind.Level, number)), Target(NumericPropertyKind.Rank, 4), Target(NumericPropertyKind.Link, 4)];
        Combo combo = new([new(new NumericPropertyRange(NumericPropertyKind.Level, 3, 5).ToCategory(), minimum, maximum)], cards: [new("Level-4", 1, 2)]);
        int total = 0;
        int successes = 0;
        for (int left = 0; left < deck.Count; left++) {
            for (int right = left + 1; right < deck.Count; right++) {
                total++;
                bool Eligible(int index) => index >= 1 && index <= 3;
                int count = (Eligible(left) ? 1 : 0) + (Eligible(right) ? 1 : 0);
                bool direct = left == 2 || right == 2;
                bool distinct = minimum == 0 || left == 2 && Eligible(right) || right == 2 && Eligible(left);
                if (direct && count >= minimum && count <= maximum && distinct) { successes++; }
            }
        }
        double actual = new ProbabilityCalculatorService().CalculateProbabilityResults(deck, [combo], 2).TotalProbability;
        Assert.That(actual, Is.EqualTo((double)successes / total).Within(1e-12));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void NumericSameCardAndOrRoutesAgreeWithIndependentPhysicalPairs(bool sameCard) {
        CategoryBase role = new("Role");
        List<Card> deck = [.. Enumerable.Range(2, 5).Select(number => Target(NumericPropertyKind.Level, number)), Target(NumericPropertyKind.Rank, 4)];
        foreach (int index in new[] { 0, 2, 5 }) { deck[index] = deck[index].WithCategories(deck[index].Categories.Append(role)); }
        CategoryBase numeric = new NumericPropertyRange(NumericPropertyKind.Level, 3, 5).ToCategory();
        Combo combo = sameCard ? new([ComboCategory.SameCard([numeric, role], 1, 2)])
            : new([], alternativeGroups: [new([ComboAlternative.For(new ComboCategory(numeric, 1, 2)), ComboAlternative.For(new ComboCard("Rank-4", 1, 2))])]);
        int total = 0;
        int successes = 0;
        for (int left = 0; left < deck.Count; left++) {
            for (int right = left + 1; right < deck.Count; right++) {
                total++;
                bool Matches(int index) => sameCard ? index == 2 : index >= 1 && index <= 3 || index == 5;
                if (Matches(left) || Matches(right)) { successes++; }
            }
        }
        double actual = new ProbabilityCalculatorService().CalculateProbabilityResults(deck, [combo], 2).TotalProbability;
        Assert.That(actual, Is.EqualTo((double)successes / total).Within(1e-12));
    }

    [Test]
    public void NumericRequirementDefinitionsAreNotOfferedAsAssignableCardMetadata() {
        CategoryBase definition = new NumericPropertyRange(NumericPropertyKind.Level, 3, 5).ToCategory();
        Combo combo = new([new(definition, 1, 3)]);
        Assert.That(CategoryCatalog.BuildOptions([], [], [combo]).All.Any(category => category.Identity == definition.Identity), Is.False);
        Assert.That(combo.Categories[0].Selector.NumericRange, Is.EqualTo(new NumericPropertyRange(NumericPropertyKind.Level, 3, 5)));
    }

    [TestCase("-1", "5")]
    [TestCase("6", "5")]
    [TestCase("null", "null")]
    [TestCase("1.5", "5")]
    [TestCase("\"1\"", "5")]
    [TestCase("2147483648", "null")]
    public void MalformedNumericRangesAreRejected(string minimum, string maximum) {
        string json = $$"""{"Kind":"NumericRange","Property":"Level","Minimum":{{minimum}},"Maximum":{{maximum}},"ObjectiveOnly":false}""";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CardSelector>(json));
    }
}
