using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class SolarRechargeEffect : ICardEffectDefinition {
    private static readonly CategoryBase Lightsworn = EffectProperties.Property("Archetype: Lightsworn", "archetype:lightsworn");
    private static readonly CategoryBase Monster = EffectProperties.Property("Monster", "kind:monster");

    public IReadOnlyList<CategoryBase> FilterProperties => [Lightsworn, Monster];
    public int CanonicalCardId => 691925;
    public string Name => "Solar Recharge";
    public int Revision => 1;
    public DrawEffectPlan Compile() => DrawEffectPlan.Draw(2, false) with {
        HandCost = 1,
        HandCostSelector = CardSelector.AllOf([Lightsworn.Identity, Monster.Identity]),
        HiddenPostDrawMill = 2
    };
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => "Discard 1 Lightsworn monster; draw 2, then send the top 2 deck cards to the GY. Requires 4 cards left.";
    public string Limitations => "Milling depletes the Deck. Search-target survival follows result accuracy: Fast may be exact or uncertain; completed physical Exact accounts for it. GY-triggered effects are not simulated.";
}
