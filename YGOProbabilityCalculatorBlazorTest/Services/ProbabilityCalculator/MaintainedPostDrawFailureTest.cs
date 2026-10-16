using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public sealed class MaintainedPostDrawFailureTest {
    private static readonly (int CardId, string EligibleProperty)[] Presets = [
        (20508881, "spell-type:quick-play"),
        (1475311, "attribute:dark")
    ];

    [TestCaseSource(nameof(Presets))]
    public void VisionAndAllureKeepUnpayableOutcomesInTheDenominatorAndResolveEligibleMaterial(
        (int CardId, string EligibleProperty) preset
    ) {
        Card source = BuiltIn(preset.CardId);
        CategoryBase eligibleProperty = new("Eligible material", CategorySource.Metadata, preset.EligibleProperty);
        Assert.That(source.EffectPlan.RemovalSelector.CategoryIdentities, Does.Contain(eligibleProperty.Identity));
        Assert.That(source.EffectPlan.FailureBehavior, Is.EqualTo(PostDrawFailureBehavior.FailBranch));

        List<Combo> targetObjective = [new([], cards: [new("target", 1, 1)])];
        Assert.That(SmallDeckOracle.MatchesHand([], targetObjective[0]), Is.False,
            "A literal whole-hand discard cannot satisfy a requirement for a final-hand card.");
        List<Card> unavailableDeck = [source, new([], id: "blank"), new([], id: "target")];
        DrawSequenceOracle.Counts unavailableOracle = DrawSequenceOracle.Enumerate(unavailableDeck, 1, targetObjective, []);
        ExactCalculationResult unavailable = Calculate(unavailableDeck, targetObjective);

        Assert.Multiple(() => {
            Assert.That(unavailableOracle.Total, Is.EqualTo(new BigInteger(6)));
            Assert.That(unavailableOracle.Failed, Is.EqualTo(new BigInteger(2)));
            Assert.That(unavailableOracle.Wins[0], Is.EqualTo(new BigInteger(2)));
            Assert.That(unavailable.Total.Numerator * unavailableOracle.Total,
                Is.EqualTo(unavailableOracle.Wins[0] * unavailable.Total.Denominator));
            Assert.That(unavailable.Total.Numerator * 3, Is.EqualTo(unavailable.Total.Denominator),
                "The two source-start outcomes remain as failures in the original six-order denominator.");
        });

        List<Card> eligibleDeck = [source, new([eligibleProperty], id: "material"), new([], id: "target")];
        DrawSequenceOracle.Counts eligibleOracle = DrawSequenceOracle.Enumerate(eligibleDeck, 1, targetObjective, []);
        ExactCalculationResult eligible = Calculate(eligibleDeck, targetObjective);

        Assert.Multiple(() => {
            Assert.That(eligibleOracle.Total, Is.EqualTo(new BigInteger(6)));
            Assert.That(eligibleOracle.Failed, Is.EqualTo(BigInteger.Zero));
            Assert.That(eligibleOracle.Wins[0], Is.EqualTo(new BigInteger(4)));
            Assert.That(eligible.Total.Numerator * eligibleOracle.Total,
                Is.EqualTo(eligibleOracle.Wins[0] * eligible.Total.Denominator));
            Assert.That(eligible.Total.Numerator * 3, Is.EqualTo(eligible.Total.Denominator * 2),
                "When the eligible card is drawn, it is removed and the target remains in hand.");
        });
    }

    [TestCaseSource(nameof(Presets))]
    public void FailBranchDiffersFromLiteralWholeHandDiscardForAnEmptyHandExclusionObjective(
        (int CardId, string EligibleProperty) preset
    ) {
        Card source = BuiltIn(preset.CardId);
        CategoryBase eligibleProperty = new("Eligible material", CategorySource.Metadata, preset.EligibleProperty);
        List<Card> deck = [source, new([], 2, id: "blank")];
        List<Combo> absenceObjective = [new([new(eligibleProperty, 0, 0)])];
        Assert.That(SmallDeckOracle.MatchesHand([], absenceObjective[0]), Is.True,
            "An empty hand satisfies this supported 0/0 exclusion objective.");

        DrawSequenceOracle.Counts oracle = DrawSequenceOracle.Enumerate(deck, 1, absenceObjective, []);
        ExactCalculationResult actual = Calculate(deck, absenceObjective);

        Assert.Multiple(() => {
            Assert.That(oracle.Total, Is.EqualTo(new BigInteger(6)));
            Assert.That(oracle.Failed, Is.EqualTo(new BigInteger(2)));
            Assert.That(oracle.Wins[0], Is.EqualTo(new BigInteger(4)));
            Assert.That(actual.Total.Numerator * oracle.Total, Is.EqualTo(oracle.Wins[0] * actual.Total.Denominator));
            Assert.That(actual.Total.Numerator * 3, Is.EqualTo(actual.Total.Denominator * 2),
                "FailBranch excludes the two source-start outcomes; literal whole-hand discard would leave an empty hand satisfying this 0/0 objective.");
        });
    }

    private static Card BuiltIn(int cardId) => CardEffectRegistry.Default.RestoreBuiltIn(
        new([], id: "effect", externalCardId: cardId));

    private static ExactCalculationResult Calculate(List<Card> deck, List<Combo> combos) =>
        new PostDrawCalculation(deck, 1, new(CalculationWorkPolicy.Interactive)).Calculate(combos, []);
}
