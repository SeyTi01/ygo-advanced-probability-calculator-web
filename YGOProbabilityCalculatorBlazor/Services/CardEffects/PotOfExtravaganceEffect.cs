using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class PotOfExtravaganceEffect : CardEffectDefinition {
    public override int CanonicalCardId => 49238328;
    public override string Name => "Pot of Extravagance";
    public override int Revision => 2;

    public override DrawEffectPlan Compile() => DrawEffectPlan.Draw(2, true) with { FirstActivationOnly = true, BlocksLaterDraws = true };
    public override EffectSupport Support => EffectSupport.ModeledWithinRules;
    public override string Limitations => ResourceText("Limitations");
}
