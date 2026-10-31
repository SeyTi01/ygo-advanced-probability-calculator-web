using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class PrimiteLordlyLodeEffect : CardEffectDefinition {
    public override int CanonicalCardId => 56506740;
    public override string Name => "Primite Lordly Lode";
    public override int Revision => 1;
    public override EffectSupport Support => EffectSupport.ModeledWithinRules;
    public override string Limitations => ResourceText("Limitations");
    public override DrawEffectPlan Compile() => DrawEffectPlan.SearchCards(
        new(CardSelector.AllOfSelectors([
            new(CardSelectorKind.Category, ["metadata:archetype:primite"]),
            CardSelector.Not(CardSelector.CatalogIdentity(56506740))
        ])),
        true
    );
}
