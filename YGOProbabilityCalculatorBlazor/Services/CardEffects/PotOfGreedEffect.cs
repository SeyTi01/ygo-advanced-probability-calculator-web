using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class PotOfGreedEffect : ICardEffectDefinition {
    public int CanonicalCardId => 55144522;
    public string Name => "Pot of Greed";
    public int Revision => 1;
    public DrawEffectPlan Compile() => DrawEffectPlan.Draw(2, false);
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => "Draw 2.";
    public string Limitations => "Automatic activation within calculator rules; no optional-play decisions.";
}
