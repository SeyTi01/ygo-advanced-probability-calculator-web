using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class RadiantTyphoonChantEffect : CardEffectDefinition {
    public override int CanonicalCardId => 67115133;
    public override string Name => "Radiant Typhoon Chant";
    public override int Revision => 3;
    public override EffectSupport Support => EffectSupport.ModeledWithinRules;
    public override DrawEffectPlan Compile() {
        CardSelector monster = CardSelector.AllOfSelectors([
            CardSelector.For(EffectProperties.Property("Archetype: Radiant Typhoon", "archetype:radiant%20typhoon")),
            new NumericPropertyRange(NumericPropertyKind.Level, 1, 4).ToSelector()
        ]);

        CardSelector typhoon = CardSelector.CatalogIdentity(5318639);

        return DrawEffectPlan.SearchCards(
            new(
                CardSelector.AnyOfSelectors([monster, typhoon]),
                true,
                [new(monster, 1), new(typhoon, 1)]
            ),
            false
        );
    }
}
