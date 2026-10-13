using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class ChickenGameEffect : ICardEffectDefinition {
    public int CanonicalCardId => 67616300;
    public string Name => "Chicken Game";
    public int Revision => 1;
    public DrawEffectPlan Compile() => DrawEffectPlan.Draw(1, false);
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => "Draw 1.";
}
