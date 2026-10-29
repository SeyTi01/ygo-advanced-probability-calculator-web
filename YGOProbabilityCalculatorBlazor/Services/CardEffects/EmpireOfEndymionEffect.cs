using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class EmpireOfEndymionEffect : CardEffectDefinition {
    public override int CanonicalCardId => 34041788;
    public override string Name => "Empire of Endymion";
    public override int Revision => 1;
    public override EffectSupport Support => EffectSupport.ModeledWithinRules;
    public override string Limitations => ResourceText("Limitations");

    public override DrawEffectPlan Compile() => DrawEffectPlan.SearchCards(
        new(CardSelector.AnyOfSelectors([
            CardSelector.CatalogIdentity(96228804),
            CardSelector.AllOfSelectors([
                CardSelector.AllOf(["metadata:kind:monster"]),
                CardSelector.Mentions("Regulus, the Prince of Endymion")
            ])
        ])),
        true
    );
}
