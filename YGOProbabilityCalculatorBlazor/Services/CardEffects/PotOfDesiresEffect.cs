using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class PotOfDesiresEffect : CardEffectDefinition {
    public override int CanonicalCardId => 35261759;
    public override string Name => "Pot of Desires";
    public override int Revision => 1;

    public override DrawEffectPlan Compile() => DrawEffectPlan.Draw(2, true) with { HiddenDeckCost = 10 };
    public override EffectSupport Support => EffectSupport.ModeledWithinRules;
    public override string Limitations => ResourceText("Limitations");
}
