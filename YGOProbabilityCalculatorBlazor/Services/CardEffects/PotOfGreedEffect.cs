using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class PotOfGreedEffect : CardEffectDefinition {
    public override int CanonicalCardId => 55144522;
    public override string Name => "Pot of Greed";
    public override int Revision => 1;
    public override DrawEffectPlan Compile() => DrawEffectPlan.Draw(2, false);
    public override EffectSupport Support => EffectSupport.ModeledWithinRules;
}
