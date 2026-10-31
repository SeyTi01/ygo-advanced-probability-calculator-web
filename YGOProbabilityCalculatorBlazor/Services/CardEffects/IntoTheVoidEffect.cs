using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class IntoTheVoidEffect : CardEffectDefinition {
    public override int CanonicalCardId => 93946239;
    public override string Name => "Into the Void";
    public override int Revision => 2;
    public override DrawEffectPlan Compile() => DrawEffectPlan.Draw(1, false) with { MinimumHandSize = 3 };
    public override EffectSupport Support => EffectSupport.ModeledWithinRules;
}
