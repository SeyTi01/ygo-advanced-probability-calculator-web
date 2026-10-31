using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class ChickenGameEffect : CardEffectDefinition {
    public override int CanonicalCardId => 67616300;
    public override string Name => "Chicken Game";
    public override int Revision => 1;
    public override DrawEffectPlan Compile() => DrawEffectPlan.Draw(1, false);
    public override EffectSupport Support => EffectSupport.ModeledWithinRules;
}
