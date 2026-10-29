using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class BrandedOpeningEffect : CardEffectDefinition {
    public override int CanonicalCardId => 36637374;
    public override string Name => "Branded Opening";
    public override int Revision => 1;
    public override EffectSupport Support => EffectSupport.ModeledWithinRules;

    public override DrawEffectPlan Compile() => DrawEffectPlan.SearchCards(new(CardSelector.AllOf(["metadata:archetype:despia", "metadata:kind:monster"])), true) with { HandCost = 1 };
}
