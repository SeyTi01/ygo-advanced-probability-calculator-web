using CardProbability.Internal;
using CardProbability.Internal.Contracts;
using CardProbability.Internal.Counting;
using CardProbability.Internal.Model;
using CardProbability.Internal.Resources;

namespace CardProbability.EngineTests;

[TestFixture]
internal sealed class SelectorPayloadBoundaryTest {
    [Test]
    public void CompiledMembershipSignaturesHaveIndependentStorageAndWorkLimits() {
        string identity = new('x', 270_000);
        Card card = new("entry", 1, [new(identity)], DrawEffectPlan.Draw(1, false));
        Combo combo = new([
            new(CardSelector.AnyOfSelectors([CardSelector.Category(identity), CardSelector.Category("absent")]), 1, 1)
        ]);
        List<Card> deck = [card];
        ProbabilityCalculationLimitException storage = Assert.Throws<ProbabilityCalculationLimitException>(() =>
            new ProbabilityCalculation(deck, 1, new(CalculationWorkPolicy.Interactive)).CalculateUnionExact([combo]))!;
        Assert.That(storage.Reason, Is.EqualTo(ProbabilityCalculationLimitReason.Storage));
        foreach (CalculationStorageTier tier in Enum.GetValues<CalculationStorageTier>()) {
            ProbabilityCalculationLimitException work = Assert.Throws<ProbabilityCalculationLimitException>(() =>
                new ProbabilityCalculation(deck, 1, new(new(1), CalculationStoragePolicy.ForTier(tier))).CalculateUnionExact([combo]))!;
            Assert.That(work.Reason, Is.EqualTo(ProbabilityCalculationLimitReason.Work));
        }
        ExactProbability actual = new ProbabilityCalculation(deck, 1, new(CalculationWorkPolicy.Interactive, CalculationStoragePolicy.High))
            .CalculateUnionExact([combo]);
        Assert.That(actual.Numerator, Is.EqualTo(actual.Denominator));
    }
}
