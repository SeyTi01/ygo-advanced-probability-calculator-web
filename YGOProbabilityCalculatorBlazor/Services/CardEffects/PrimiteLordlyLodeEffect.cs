using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class PrimiteLordlyLodeEffect : ICardEffectDefinition {
    public int CanonicalCardId => 56506740;
    public string Name => "Primite Lordly Lode";
    public int Revision => 1;
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => "On activation, search 1 other Primite card";
    public string Limitations => "Models the on-activation Deck search; its separate Normal Monster Special Summon effect is outside scope.";

    public DrawEffectPlan Compile() => new(null, true, 0, CardSelector.Any, 0, 0, CardSelector.Any,
        Search: new(CardSelector.AllOfSelectors([
            new(CardSelectorKind.Category, ["metadata:archetype:primite"]),
            CardSelector.Not(CardSelector.CatalogName("Primite Lordly Lode"))
        ])));
}
