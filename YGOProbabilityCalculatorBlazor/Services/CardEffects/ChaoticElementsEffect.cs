using YGOProbabilityCalculatorBlazor.Localization;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class ChaoticElementsEffect : ICardEffectDefinition {
    public int CanonicalCardId => 92221402;
    public string Name => "Chaotic Elements";
    public int Revision => 3;
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => UiText.English("Effects.92221402.Summary");
    public string Limitations => UiText.English("Effects.92221402.Limitations");

    public DrawEffectPlan Compile() => new(null, true, 0, CardSelector.Any, 0, 0, CardSelector.Any,
        Search: new(CardSelector.AllOfSelectors([
            CardSelector.LevelAtLeast(5),
            CardSelector.AnyOf(["metadata:attribute:light", "metadata:attribute:dark"]),
            CardSelector.AnyOf(["metadata:monster-race:pyro", "metadata:monster-race:aqua"])
        ])));
}
