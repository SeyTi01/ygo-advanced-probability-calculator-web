using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class SolarRechargeEffect : CardEffectDefinition {
    public override IReadOnlyList<CategoryBase> FilterProperties => [Lightsworn, Monster];
    public override int CanonicalCardId => 691925;
    public override string Name => "Solar Recharge";
    public override int Revision => 1;
    public override EffectSupport Support => EffectSupport.ModeledWithinRules;
    public override string Limitations => ResourceText("Limitations");
    public override DrawEffectPlan Compile() => DrawEffectPlan.Draw(2, false) with {
        HandCost = 1,
        HandCostSelector = CardSelector.AllOf([Lightsworn.Identity, Monster.Identity]),
        HiddenPostDrawMill = 2
    };

    private static readonly CategoryBase Lightsworn = EffectProperties.Property("Archetype: Lightsworn", "archetype:lightsworn");
    private static readonly CategoryBase Monster = EffectProperties.Property("Monster", "kind:monster");
}
