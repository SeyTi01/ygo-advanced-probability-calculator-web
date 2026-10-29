using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class OneDayOfPeaceEffect : CardEffectDefinition {
    public override int CanonicalCardId => 33782437;
    public override string Name => "One Day of Peace";
    public override int Revision => 1;

    public override DrawEffectPlan Compile() => DrawEffectPlan.Draw(1, false);
    public override EffectSupport Support => EffectSupport.ModeledWithinRules;
}
