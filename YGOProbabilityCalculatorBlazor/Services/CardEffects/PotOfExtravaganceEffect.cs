using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class PotOfExtravaganceEffect : ICardEffectDefinition {
    public int CanonicalCardId => 49238328;
    public string Name => "Pot of Extravagance";
    public int Revision => 1;
    public DrawEffectPlan Compile() => DrawEffectPlan.Draw(2, true);
    public EffectSupport Support => EffectSupport.Partial;
    public string Summary => "Draw 2; once per turn.";
    public string Limitations => "Activation timing and the restriction on subsequent draws are not enforced.";
}
