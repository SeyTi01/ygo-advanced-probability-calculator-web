using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class PotOfProsperityEffect : CardEffectDefinition {
    public override int CanonicalCardId => 84211599;
    public override string Name => "Pot of Prosperity";
    public override int Revision => 1;
    public override DrawEffectPlan Compile() => DrawEffectPlan.Excavate(6, requiresNoPriorEffectDraw: true);
    public override EffectSupport Support => EffectSupport.ModeledWithinRules;
    public override string Limitations => ResourceText("Limitations");
}
