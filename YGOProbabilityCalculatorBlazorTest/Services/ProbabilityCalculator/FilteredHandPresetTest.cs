using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;
using YGOProbabilityCalculatorBlazor.Services.DeckImport;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public sealed class FilteredHandPresetTest {
    private sealed record PresetCase(ICardEffectDefinition Definition, CardInfo PositiveTarget, CardInfo NegativeTarget,
        bool OncePerTurn, CardInfo? AlternatePositiveTarget = null);

    private static readonly PresetCase[] Presets = [
        new(new FiendsmithEngraverEffect(), Info(98567237, "Fiendsmith's Tract", "Spell Card", "Normal", archetypes: ["Fiendsmith"]),
            Info(85106525, "Bonfire", "Spell Card", "Normal"), true,
            Info(26434972, "Fiendsmith Kyrie", "Trap Card", "Normal", archetypes: ["Fiendsmith"])),
        new(new EarthboundPrisonerStoneSweeperEffect(), Info(19322865, "Earthbound Prisoner Line Walker", "Tuner Monster", "Fiend", "DARK", 3),
            Info(13521194, "Stygian Street Patrol", "Effect Monster", "Fiend", "DARK", 4), true),
        new(new PreparationOfRitesEffect(), Info(4810828, "Sauravis, the Ancient and Ascended", "Ritual Effect Monster", "Dragon", "LIGHT", 7),
            Info(79613121, "Magician of Black Chaos MAX", "Ritual Effect Monster", "Spellcaster", "DARK", 8, archetypes: ["Chaos", "Magician"]), false),
        new(new FireFormationTenkiEffect(), Info(6353603, "Brotherhood of the Fire Fist - Bear", "Effect Monster", "Beast-Warrior", "FIRE", 4,
                archetypes: ["Fire Fist"]),
            Info(61472381, "Brotherhood of the Fire Fist - Eland", "Ritual Effect Monster", "Beast-Warrior", "FIRE", 6,
                archetypes: ["Fire Fist"]), true),
        new(new BonfireEffect(), Info(90241276, "Snake-Eyes Poplar", "Effect Monster", "Pyro", "FIRE", 1, archetypes: ["Snake-Eye"]),
            Info(32543380, "Volcanic Doomfire", "Effect Monster", "Pyro", "FIRE", 8, archetypes: ["Volcanic"]), true)
    ];

    private static readonly CardEffectRegistry Registry = new(Presets.Select(preset => preset.Definition));

    [Test]
    public void DefinitionsUseVerifiedCatalogIdentitiesAndObjectiveTargetMetadata() {
        foreach (PresetCase preset in Presets) {
            ICardEffectDefinition definition = preset.Definition;
            CardInfo sourceInfo = SourceInfo(definition);
            MaintainedCardEffect maintained = Registry.Find(sourceInfo)!;
            Card source = Registry.CreateImportedCard(sourceInfo, 2, sourceInfo.Id);
            Card selected = Registry.ApplyForExplicitSelection(new([], id: "selected"), new([], id: "previous"), sourceInfo);
            Card customized = source.WithOncePerTurn(!preset.OncePerTurn);
            Card restored = Registry.RestoreBuiltIn(customized);
            Card positive = FromInfo(preset.PositiveTarget, "positive");
            Card negative = FromInfo(preset.NegativeTarget, "negative");
            Card? alternatePositive = preset.AlternatePositiveTarget is { } alternateInfo ? FromInfo(alternateInfo, "alternate-positive") : null;
            Card spoofed = new([], id: "spoofed", externalCardId: preset.PositiveTarget.Id);
            foreach (CategoryBase category in positive.Categories.Where(category => category.Source == CategorySource.Metadata)) {
                spoofed = spoofed.WithManualMetadataCategory(category);
            }

            Assert.Multiple(() => {
                Assert.That(definition.CanonicalCardId, Is.EqualTo(sourceInfo.Id), definition.Name);
                Assert.That(definition.Aliases, Is.Empty, "YGOPRODeck lists the canonical passcode and no alternate passcodes for these five cards.");
                Assert.That(sourceInfo.CanonicalCardId, Is.EqualTo(sourceInfo.Id), definition.Name);
                Assert.That(sourceInfo.ArtworkImageIds, Is.EqualTo(new[] { sourceInfo.Id }), definition.Name);
                Assert.That(maintained.CanonicalCardId, Is.EqualTo(definition.CanonicalCardId), definition.Name);
                Assert.That(maintained.Plan.DrawCount, Is.Null, definition.Name);
                Assert.That(maintained.Plan.Search, Is.Not.Null, definition.Name);
                Assert.That(maintained.Plan.Search!.ObjectiveMetadataOnly, Is.True, definition.Name);
                Assert.That(maintained.Plan.OncePerTurn, Is.EqualTo(preset.OncePerTurn), definition.Name);
                Assert.That(source.EffectProfile.Source, Is.EqualTo(EffectSource.BuiltIn), definition.Name);
                Assert.That(source.EffectPlan.HandCost, Is.Zero, "Discarding the activating source is its search operation, not an extra hand cost.");
                Assert.That(selected.EffectPlan, Is.EqualTo(source.EffectPlan), definition.Name);
                Assert.That(restored.EffectPlan, Is.EqualTo(source.EffectPlan), definition.Name);
                Assert.That(restored.EffectProfile.Source, Is.EqualTo(EffectSource.BuiltIn), definition.Name);
                Assert.That(source.Search!.Matches(positive), Is.True, definition.Name + " positive target");
                Assert.That(alternatePositive is null || source.Search.Matches(alternatePositive), Is.True,
                    definition.Name + " alternate positive target");
                Assert.That(source.Search.Matches(negative), Is.False, definition.Name + " negative target");
                Assert.That(source.Search.Matches(spoofed), Is.False, definition.Name + " manual metadata must not grant search eligibility");
                Assert.That(source.Search.Matches(new(positive.Categories, externalCardId: null)), Is.False,
                    definition.Name + " requires recognized catalog identity");
                Assert.That(definition.Support, Is.EqualTo(EffectSupport.ModeledWithinRules), definition.Name);
                Assert.That(definition.Limitations, Is.Empty, "Excluded effects do not alter the opening-hand search model.");
            });
        }
    }

    [Test]
    public void FastAndExactSearchProbabilitiesMatchTheLabeledPhysicalOracle() {
        foreach (PresetCase preset in Presets) {
            CardInfo sourceInfo = SourceInfo(preset.Definition);
            Card source = Registry.CreateImportedCard(sourceInfo, 1, sourceInfo.Id);
            Card positive = FromInfo(preset.PositiveTarget, "positive");
            Card negative = FromInfo(preset.NegativeTarget, "negative");
            Card blank = new([], id: "blank");
            List<Card> deck = [source, positive, negative, blank];
            List<Combo> combo = [new([], "find positive", cards: [new(positive.Id, 1, 1)])];

            AssertModesMatchOracle(deck, combo, 1, new(1, 2), preset.Definition.Name + " search access");
            if (preset.AlternatePositiveTarget is { } alternateInfo) {
                Card alternatePositive = FromInfo(alternateInfo, "alternate-positive");
                List<Card> alternateDeck = [source, alternatePositive, negative, blank];
                List<Combo> alternateCombo = [new([], "find alternate target", cards: [new(alternatePositive.Id, 1, 1)])];
                AssertModesMatchOracle(alternateDeck, alternateCombo, 1, new(1, 2), preset.Definition.Name + " alternate target");
            }
            AssertModesMatchOracle([source, negative, blank], combo, 1, (BigInteger.Zero, BigInteger.One),
                preset.Definition.Name + " no legal Deck target");

            // If a legal search were modeled as an additional hand card, it
            // could satisfy this row while retaining its source. Requiring both
            // source and target proves that activation consumes the source.
            List<Combo> sourceAndTarget = [new([], "source and target", cards: [new(source.Id, 1, 1), new(positive.Id, 1, 1)])];
            AssertModesMatchOracle(deck, sourceAndTarget, 2, new(1, 6), preset.Definition.Name + " source is consumed");

            Card twoSources = source.WithCopies(2);
            Card twoTargets = positive.WithCopies(2);
            Card twoBlanks = blank.WithCopies(2);
            List<Combo> twoTargetsRequired = [new([], "two targets", cards: [new(twoTargets.Id, 2, 2)])];
            BigInteger optWins = preset.OncePerTurn ? new(5) : new(6);
            AssertModesMatchOracle([twoSources, twoTargets, twoBlanks], twoTargetsRequired, 2,
                (optWins, new BigInteger(15)), preset.Definition.Name + " per-entry activation allowance");
        }
    }

    private static void AssertModesMatchOracle(List<Card> deck, List<Combo> combos, int handSize,
        (BigInteger Numerator, BigInteger Denominator) expected, string description) {
        (BigInteger total, BigInteger[] wins) = SearchAccessOracle.Count(deck, combos, handSize, [], null);
        Assert.That(wins[0] * expected.Denominator, Is.EqualTo(expected.Numerator * total), description + " independent oracle");

        ProbabilityCalculatorService calculator = new();
        foreach (CalculationModel mode in new[] { CalculationModel.Fast, CalculationModel.Exact }) {
            ProbabilityCalculationResult result = calculator.CalculateProbabilityResults(deck, combos, handSize, [],
                CalculationWorkPolicy.Interactive, options: new(Mode: mode));
            Assert.That(result.TotalProbability, Is.EqualTo((double)expected.Numerator / (double)expected.Denominator)
                .Within(1e-12), description + " " + mode);
            Assert.That(result.Contract!.Accuracy, Is.EqualTo(CalculationAccuracy.Exact), description + " " + mode + " proof");
        }
    }

    private static CardInfo SourceInfo(ICardEffectDefinition definition) => definition.CanonicalCardId switch {
        60764609 => Info(60764609, "Fiendsmith Engraver", "Effect Monster", "Fiend", "LIGHT", 6, archetypes: ["Fiendsmith"]),
        72323266 => Info(72323266, "Earthbound Prisoner Stone Sweeper", "Effect Monster", "Fiend", "DARK", 5,
            archetypes: ["Earthbound"]),
        96729612 => Info(96729612, "Preparation of Rites", "Spell Card", "Normal"),
        57103969 => Info(57103969, "Fire Formation - Tenki", "Spell Card", "Continuous", archetypes: ["Fire Formation"]),
        85106525 => Info(85106525, "Bonfire", "Spell Card", "Normal"),
        _ => throw new ArgumentOutOfRangeException(nameof(definition))
    };

    private static CardInfo Info(int id, string name, string type, string race, string? attribute = null, int? level = null,
        string[]? archetypes = null) => new() {
            Id = id,
            CanonicalCardId = id,
            ArtworkImageIds = [id],
            ArtworkMetadataKnown = true,
            Name = name,
            Type = type,
            FrameType = type switch {
                "Spell Card" => "spell",
                "Trap Card" => "trap",
                "Ritual Effect Monster" => "ritual",
                "Tuner Monster" => "effect",
                "Effect Monster" => "effect",
                _ => null
            },
            Race = race,
            Attribute = attribute,
            Level = level,
            ResolvedArchetypes = archetypes ?? []
        };

    private static Card FromInfo(CardInfo info, string id) => new(CardPropertyProvider.GetCategories(info),
        name: info.Name, id: id, externalCardId: info.Id);
}
