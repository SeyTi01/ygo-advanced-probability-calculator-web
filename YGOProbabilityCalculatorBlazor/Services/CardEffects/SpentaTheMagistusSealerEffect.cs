using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class SpentaTheMagistusSealerEffect : ICardEffectDefinition {
    public int CanonicalCardId => 42544773;
    public string Name => "Spenta, the Magistus Sealer";
    public int Revision => 1;
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => "Discard this card; search 1 other Magistus monster";
    public string Limitations => "Models the discard-and-search mode; its ATK-reduction and GY Equip effects are outside scope.";

    public DrawEffectPlan Compile() => new(null, true, 0, CardSelector.Any, 0, 0, CardSelector.Any,
        Search: new(CardSelector.AllOfSelectors([
            CardSelector.AllOf(["metadata:kind:monster", "metadata:archetype:magistus"]),
            CardSelector.Not(CardSelector.CatalogName("Spenta, the Magistus Sealer"))
        ])));
}
