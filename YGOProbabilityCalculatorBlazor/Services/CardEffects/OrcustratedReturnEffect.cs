using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class OrcustratedReturnEffect : ICardEffectDefinition {
    public int CanonicalCardId => 26845680;
    public string Name => "Orcustrated Return";
    public int Revision => 1;
    public DrawEffectPlan Compile() => DrawEffectPlan.Draw(2, true) with {
        HandCost = 1,
        HandCostSelector = CardSelector.AnyOfSelectors([
            CardSelector.AllOf(["metadata:kind:monster", "metadata:archetype:orcust"]),
            CardSelector.AllOf(["metadata:kind:monster", "metadata:archetype:world%20legacy"])])
    };
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => "Remove an Orcust or World Legacy monster from hand before drawing 2; once per turn.";
}
