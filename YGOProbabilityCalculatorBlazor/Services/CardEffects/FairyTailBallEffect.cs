using YGOProbabilityCalculatorBlazor.Localization;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class FairyTailBallEffect : ICardEffectDefinition {
    public int CanonicalCardId => 56725612;
    public string Name => "Fairy Tail Ball";
    public int Revision => 1;
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => UiText.English("Effects.56725612.Summary");
    public string Limitations => UiText.English("Effects.56725612.Limitations");

    public DrawEffectPlan Compile() => new(null, true, 0, CardSelector.Any, 0, 0, CardSelector.Any,
        Search: new(CardSelector.AnyOfSelectors([
            CardSelector.AllOf(["metadata:kind:monster", "metadata:archetype:fairy%20tail"]),
            CardSelector.CatalogIdentity(91957038)
        ])));
}
