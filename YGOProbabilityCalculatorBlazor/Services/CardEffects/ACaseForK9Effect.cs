using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class ACaseForK9Effect : ICardEffectDefinition {
    public int CanonicalCardId => 80181649;
    public string Name => "\"A Case for K9\"";
    public int Revision => 1;
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => "Search 1 K9 monster";
    public DrawEffectPlan Compile() => new(null, true, 0, CardSelector.Any, 0, 0, CardSelector.Any,
        Search: new(CardSelector.AllOf(["metadata:kind:monster", "metadata:archetype:k9"])));
}
