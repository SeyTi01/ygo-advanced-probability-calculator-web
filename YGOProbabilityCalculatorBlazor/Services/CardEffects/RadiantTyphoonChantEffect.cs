using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class RadiantTyphoonChantEffect : ICardEffectDefinition {
    public int CanonicalCardId => 67115133;
    public string Name => "Radiant Typhoon Chant";
    public int Revision => 1;
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => "Search a Level 1–4 Radiant Typhoon monster or Mystical Space Typhoon; each mode once per entry";

    public DrawEffectPlan Compile() {
        CardSelector monster = CardSelector.AllOfSelectors([
            CardSelector.AllOf(["metadata:archetype:radiant%20typhoon", "metadata:kind:monster"]),
            CardSelector.AnyOf(["metadata:level:1", "metadata:level:2", "metadata:level:3", "metadata:level:4"])
        ]);
        CardSelector typhoon = CardSelector.CatalogIdentity(5318639);

        return new(null, false, 0, CardSelector.Any, 0, 0, CardSelector.Any,
            Search: new(CardSelector.AnyOfSelectors([monster, typhoon]), true,
                new SearchEffectMode[] { new(monster, 1), new(typhoon, 1) }));
    }
}
