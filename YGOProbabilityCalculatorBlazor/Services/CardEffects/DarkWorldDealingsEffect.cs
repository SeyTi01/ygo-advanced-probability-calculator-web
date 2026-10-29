using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class DarkWorldDealingsEffect : CardEffectDefinition {
    public override int CanonicalCardId => 74117290;
    public override string Name => "Dark World Dealings";
    public override int Revision => 1;

    public override DrawEffectPlan Compile() => DrawEffectPlan.Draw(1, false) with { RemovalCount = 1 };
    public override EffectSupport Support => EffectSupport.ModeledWithinRules;
}
