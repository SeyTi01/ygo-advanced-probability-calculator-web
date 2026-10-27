using YGOProbabilityCalculatorBlazor.Localization;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class RadiantTyphoonChantEffect : ICardEffectDefinition {
    public int CanonicalCardId => 67115133;
    public string Name => "Radiant Typhoon Chant";
    public int Revision => 3;
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => UiText.English("Effects.67115133.Summary");

    public DrawEffectPlan Compile() {
        CardSelector monster = CardSelector.AllOfSelectors([
            CardSelector.For(EffectProperties.Property("Archetype: Radiant Typhoon", "archetype:radiant%20typhoon")),
            new NumericPropertyRange(NumericPropertyKind.Level, 1, 4).ToSelector()
        ]);
        CardSelector typhoon = CardSelector.CatalogIdentity(5318639);

        return new(null, false, 0, CardSelector.Any, 0, 0, CardSelector.Any,
            Search: new(CardSelector.AnyOfSelectors([monster, typhoon]), true,
                new SearchEffectMode[] { new(monster, 1), new(typhoon, 1) }));
    }
}
