using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class OrcustratedReturnEffect : CardEffectDefinition {
    public override IReadOnlyList<CategoryBase> FilterProperties => [Monster, Orcust, WorldLegacy];
    public override int CanonicalCardId => 26845680;
    public override string Name => "Orcustrated Return";
    public override int Revision => 2;
    public override EffectSupport Support => EffectSupport.ModeledWithinRules;
    public override DrawEffectPlan Compile() => DrawEffectPlan.Draw(2, true) with {
        HandCost = 1,
        HandCostSelector = CardSelector.AllOfSelectors([
            CardSelector.For(Monster),
            CardSelector.AnyOf([Orcust.Identity, WorldLegacy.Identity])
        ])
    };

    private static readonly CategoryBase Monster = EffectProperties.Property("Monster", "kind:monster");
    private static readonly CategoryBase Orcust = EffectProperties.Property("Archetype: Orcust", "archetype:orcust");
    private static readonly CategoryBase WorldLegacy = EffectProperties.Property("Archetype: World Legacy", "archetype:world%20legacy");
}
