using YGOProbabilityCalculatorBlazor.Localization;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class BrandedOpeningEffect : ICardEffectDefinition {
    public int CanonicalCardId => 36637374;
    public string Name => "Branded Opening";
    public int Revision => 1;
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => UiText.English("Effects.36637374.Summary");
    public DrawEffectPlan Compile() => new(null, true, 1, CardSelector.Any, 0, 0, CardSelector.Any,
        Search: new(CardSelector.AllOf(["metadata:archetype:despia", "metadata:kind:monster"])));
}
