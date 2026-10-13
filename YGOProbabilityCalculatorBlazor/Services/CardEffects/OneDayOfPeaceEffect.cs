using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class OneDayOfPeaceEffect : ICardEffectDefinition {
    public int CanonicalCardId => 33782437;
    public string Name => "One Day of Peace";
    public int Revision => 1;
    public DrawEffectPlan Compile() => DrawEffectPlan.Draw(1, false);
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => "Draw 1.";
}
