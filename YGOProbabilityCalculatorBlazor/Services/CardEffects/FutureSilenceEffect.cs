using YGOProbabilityCalculatorBlazor.Localization;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class FutureSilenceEffect : ICardEffectDefinition {
    public int CanonicalCardId => 78679226;
    public string Name => "Future Silence";
    public int Revision => 1;
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => UiText.English("Effects.78679226.Summary");
    public string Limitations => UiText.English("Effects.78679226.Limitations");

    public DrawEffectPlan Compile() => new(null, true, 0, CardSelector.Any, 0, 0, CardSelector.Any,
        Search: new(CardSelector.AllOfSelectors([
            CardSelector.AllOf(["metadata:kind:monster"]),
            CardSelector.Mentions("Shining Sarcophagus")
        ])));
}
