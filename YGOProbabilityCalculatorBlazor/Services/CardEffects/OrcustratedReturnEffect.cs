using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class OrcustratedReturnEffect : ICardEffectDefinition {
    private static readonly CategoryBase Monster = EffectProperties.Property("Monster", "kind:monster");
    private static readonly CategoryBase Orcust = EffectProperties.Property("Archetype: Orcust", "archetype:orcust");
    private static readonly CategoryBase WorldLegacy = EffectProperties.Property("Archetype: World Legacy", "archetype:world%20legacy");

    public IReadOnlyList<CategoryBase> FilterProperties => [Monster, Orcust, WorldLegacy];
    public int CanonicalCardId => 26845680;
    public string Name => "Orcustrated Return";
    public int Revision => 2;
    public DrawEffectPlan Compile() => DrawEffectPlan.Draw(2, true) with {
        HandCost = 1,
        HandCostSelector = CardSelector.AllOfSelectors([
            CardSelector.For(Monster),
            CardSelector.AnyOf([Orcust.Identity, WorldLegacy.Identity])
        ])
    };
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => "Remove an Orcust or World Legacy monster from hand before drawing 2; once per turn.";
}
