using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class SkyStrikerMobilizeEngageEffect : CardEffectDefinition {
    private static readonly CategoryBase SkyStriker = EffectProperties.Property("Archetype: Sky Striker", "archetype:sky%20striker");

    public override int CanonicalCardId => 63166095;
    public override string Name => "Sky Striker Mobilize - Engage!";
    public override int Revision => 1;
    public override IReadOnlyList<CategoryBase> FilterProperties => [SkyStriker];
    public override EffectSupport Support => EffectSupport.ModeledWithinRules;
    public override string Limitations => ResourceText("Limitations");

    public override DrawEffectPlan Compile() => DrawEffectPlan.SearchCards(
        new(CardSelector.AllOfSelectors([
            CardSelector.For(SkyStriker),
            CardSelector.Not(CardSelector.CatalogIdentity(63166095))
        ])),
        false
    );
}
