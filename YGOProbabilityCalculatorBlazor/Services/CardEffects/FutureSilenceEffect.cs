using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class FutureSilenceEffect : ICardEffectDefinition {
    public int CanonicalCardId => 78679226;
    public string Name => "Future Silence";
    public int Revision => 1;
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => "Search 1 monster that mentions Shining Sarcophagus";
    public string Limitations => "The conditional Battle Phase draw is outside the search-only opening-hand model.";

    public DrawEffectPlan Compile() => new(null, true, 0, CardSelector.Any, 0, 0, CardSelector.Any,
        Search: new(CardSelector.AllOfSelectors([
            CardSelector.AllOf(["metadata:kind:monster"]),
            CardSelector.Mentions("Shining Sarcophagus")
        ])));
}
