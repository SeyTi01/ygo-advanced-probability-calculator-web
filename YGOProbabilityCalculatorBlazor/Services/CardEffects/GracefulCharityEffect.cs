using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class GracefulCharityEffect : ICardEffectDefinition {
    public int CanonicalCardId => 79571449;
    public string Name => "Graceful Charity";
    public int Revision => 1;
    public DrawEffectPlan Compile() => DrawEffectPlan.Draw(3, false) with { RemovalCount = 2 };
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => "Draw 3, then remove 2 cards from hand.";
    public string Limitations => "Uses deterministic removal; discard triggers and graveyard state are not modeled.";
}
