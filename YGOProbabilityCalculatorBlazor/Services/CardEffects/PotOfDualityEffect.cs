using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class PotOfDualityEffect : CardEffectDefinition {
    public override int CanonicalCardId => 98645731;
    public override string Name => "Pot of Duality";
    public override int Revision => 1;
    public override DrawEffectPlan Compile() => DrawEffectPlan.Excavate(3);
    public override EffectSupport Support => EffectSupport.ModeledWithinRules;
    public override string Limitations => ResourceText("Limitations");
}
