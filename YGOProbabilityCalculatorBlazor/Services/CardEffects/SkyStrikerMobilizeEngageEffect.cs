using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class SkyStrikerMobilizeEngageEffect : ICardEffectDefinition {
    private static readonly CategoryBase SkyStriker = EffectProperties.Property("Archetype: Sky Striker", "archetype:sky%20striker");

    public int CanonicalCardId => 63166095;
    public string Name => "Sky Striker Mobilize - Engage!";
    public int Revision => 1;
    public IReadOnlyList<CategoryBase> FilterProperties => [SkyStriker];
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => "Search 1 other Sky Striker card from the Deck; no once-per-turn limit.";
    public string Limitations => "Assumes no monsters in your Main Monster Zones; the optional draw with 3 or more Spells in the GY is outside this search model.";

    public DrawEffectPlan Compile() => new(null, false, 0, CardSelector.Any, 0, 0, CardSelector.Any,
        Search: new(CardSelector.AllOfSelectors([
            CardSelector.For(SkyStriker),
            CardSelector.Not(CardSelector.CatalogName(Name))
        ])));
}
