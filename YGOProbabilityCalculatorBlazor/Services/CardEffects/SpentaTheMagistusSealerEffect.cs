using YGOProbabilityCalculatorBlazor.Localization;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class SpentaTheMagistusSealerEffect : ICardEffectDefinition {
    public int CanonicalCardId => 42544773;
    public string Name => "Spenta, the Magistus Sealer";
    public int Revision => 1;
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => UiText.English("Effects.42544773.Summary");
    public string Limitations => UiText.English("Effects.42544773.Limitations");

    public DrawEffectPlan Compile() => new(null, true, 0, CardSelector.Any, 0, 0, CardSelector.Any,
        Search: new(CardSelector.AllOfSelectors([
            CardSelector.AllOf(["metadata:kind:monster", "metadata:archetype:magistus"]),
            CardSelector.Not(CardSelector.CatalogIdentity(42544773))
        ])));
}
