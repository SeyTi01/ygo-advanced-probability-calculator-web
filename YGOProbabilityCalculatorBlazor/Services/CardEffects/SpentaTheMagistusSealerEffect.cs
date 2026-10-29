using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class SpentaTheMagistusSealerEffect : CardEffectDefinition {
    public override int CanonicalCardId => 42544773;
    public override string Name => "Spenta, the Magistus Sealer";
    public override int Revision => 1;
    public override EffectSupport Support => EffectSupport.ModeledWithinRules;
    public override string Limitations => ResourceText("Limitations");

    public override DrawEffectPlan Compile() => DrawEffectPlan.SearchCards(
        new(CardSelector.AllOfSelectors([
            CardSelector.AllOf(["metadata:kind:monster", "metadata:archetype:magistus"]),
            CardSelector.Not(CardSelector.CatalogIdentity(42544773))
        ])),
        true
    );
}
