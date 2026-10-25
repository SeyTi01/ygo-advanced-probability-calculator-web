using YGOProbabilityCalculatorBlazor.Localization;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class SkyStrikerMobilizeEngageEffect : ICardEffectDefinition {
    private static readonly CategoryBase SkyStriker = EffectProperties.Property("Archetype: Sky Striker", "archetype:sky%20striker");

    public int CanonicalCardId => 63166095;
    public string Name => "Sky Striker Mobilize - Engage!";
    public int Revision => 1;
    public IReadOnlyList<CategoryBase> FilterProperties => [SkyStriker];
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => UiText.English("Effects.63166095.Summary");
    public string Limitations => UiText.English("Effects.63166095.Limitations");

    public DrawEffectPlan Compile() => new(null, false, 0, CardSelector.Any, 0, 0, CardSelector.Any,
        Search: new(CardSelector.AllOfSelectors([
            CardSelector.For(SkyStriker),
            CardSelector.Not(CardSelector.CatalogIdentity(63166095))
        ])));
}
