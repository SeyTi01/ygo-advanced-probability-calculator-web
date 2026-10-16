using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class BrandedOpeningEffect : ICardEffectDefinition {
    public int CanonicalCardId => 36637374;
    public string Name => "Branded Opening";
    public int Revision => 1;
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => "Search a Despia monster after discarding 1 other card";
    public DrawEffectPlan Compile() => new(null, true, 1, CardSelector.Any, 0, 0, CardSelector.Any,
        Search: new(CardSelector.AllOf(["metadata:archetype:despia", "metadata:kind:monster"])));
}
