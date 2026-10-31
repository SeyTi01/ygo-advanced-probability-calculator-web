using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class UpstartGoblinEffect : CardEffectDefinition {
    public override int CanonicalCardId => 70368879;
    public override string Name => "Upstart Goblin";
    public override int Revision => 1;
    public override DrawEffectPlan Compile() => DrawEffectPlan.Draw(1, false);
    public override EffectSupport Support => EffectSupport.ModeledWithinRules;
}
