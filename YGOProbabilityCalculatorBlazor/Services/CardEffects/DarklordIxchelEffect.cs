using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class DarklordIxchelEffect : CardEffectDefinition {
    private static readonly CategoryBase Darklord = EffectProperties.Property("Archetype: Darklord", "archetype:darklord");
    public override int CanonicalCardId => 52840267;
    public override string Name => "Darklord Ixchel";
    public override int Revision => 1;
    public override IReadOnlyList<CategoryBase> FilterProperties => [Darklord];
    public override EffectSupport Support => EffectSupport.ModeledWithinRules;

    public override DrawEffectPlan Compile() => DrawEffectPlan.Draw(2, true) with {
        HandCost = 1,
        HandCostSelector = CardSelector.Objective(CardSelector.For(Darklord))
    };
}
