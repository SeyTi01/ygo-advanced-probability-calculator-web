using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class ACaseForK9Effect : CardEffectDefinition {
    public override int CanonicalCardId => 80181649;
    public override string Name => "\"A Case for K9\"";
    public override int Revision => 1;
    public override EffectSupport Support => EffectSupport.ModeledWithinRules;
    public override DrawEffectPlan Compile() => DrawEffectPlan.SearchCards(new(CardSelector.AllOf(["metadata:kind:monster", "metadata:archetype:k9"])), true);
}
