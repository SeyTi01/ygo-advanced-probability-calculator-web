using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class GracefulCharityEffect : CardEffectDefinition {
    public override int CanonicalCardId => 79571449;
    public override string Name => "Graceful Charity";
    public override int Revision => 1;
    public override DrawEffectPlan Compile() => DrawEffectPlan.Draw(3, false) with { RemovalCount = 2 };
    public override EffectSupport Support => EffectSupport.ModeledWithinRules;
}
