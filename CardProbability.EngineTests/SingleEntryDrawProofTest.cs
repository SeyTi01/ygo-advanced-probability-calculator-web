using CardProbability.EngineTests.Fixtures;
using CardProbability.Internal;
using CardProbability.Internal.Model;

namespace CardProbability.EngineTests;

[TestFixture]
internal sealed class SingleEntryDrawProofTest {
    [Test]
    public void OneLimitedEntryMakesItsTimingRestrictionsRedundantOnlyWhenWholeBatchFits() {
        EffectPlan limited = new(DrawCount: 2, ActivationLimit: 1, FirstActivationOnly: true, BlocksLaterDraws: true);
        Assert.That(Proof([new("source", 3, limited)], 40, 5), Is.True);
        Assert.That(Proof([new("source", 3, limited)], 6, 5), Is.False);
        Assert.That(Proof([new("source", 2, limited), new("independent", 1, limited)], 40, 5), Is.False);
        Assert.That(Proof([new("source", 3, limited), new("other", 1, new(DrawCount: 1))], 40, 5), Is.False);
        Assert.That(Proof([new("source", 3, limited with { ActivationLimit = null })], 40, 5), Is.False);
        Assert.That(Proof([new("source", 3, limited with { HandCost = 1 })], 40, 5), Is.False);
        Assert.That(Proof([new("source", 3, limited with { RemovalCount = 1 })], 40, 5), Is.False);
    }

    private static bool Proof(List<DeckEntry> effects, int total, int hand) {
        List<DeckEntry> entries = [.. effects, new("filler", total - effects.Sum(entry => entry.Copies))];
        PreparedCalculation model = new EngineCase("single-entry", new(entries, hand, []), new()).Prepare();
        List<Card> sources = model.Cards.Where(card => card.DrawCount is not null).ToList();
        Assert.That(sources[0].FirstActivationOnly && sources[0].BlocksLaterDraws, Is.True);
        return DrawEffectResolver.CanUseCountStateFastPath(sources, total, hand);
    }
}
