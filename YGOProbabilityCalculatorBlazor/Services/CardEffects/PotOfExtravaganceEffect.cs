using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class PotOfExtravaganceEffect : ICardEffectDefinition {
    public int CanonicalCardId => 49238328;
    public string Name => "Pot of Extravagance";
    public int Revision => 2;
    public DrawEffectPlan Compile() => DrawEffectPlan.Draw(2, true) with { FirstActivationOnly = true, BlocksLaterDraws = true };
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => "First draw activation only; draw 2; no further draws.";
}
