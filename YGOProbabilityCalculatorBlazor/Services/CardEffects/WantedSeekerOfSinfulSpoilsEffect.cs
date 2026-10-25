using YGOProbabilityCalculatorBlazor.Localization;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class WantedSeekerOfSinfulSpoilsEffect : ICardEffectDefinition {
    public int CanonicalCardId => 80845034;
    public string Name => "WANTED: Seeker of Sinful Spoils";
    public int Revision => 1;
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => UiText.English("Effects.80845034.Summary");
    public string Limitations => UiText.English("Effects.80845034.Limitations");

    public DrawEffectPlan Compile() => new(null, true, 0, CardSelector.Any, 0, 0, CardSelector.Any,
        Search: new(CardSelector.AllOf(["metadata:kind:monster", "metadata:archetype:diabellstar"])));
}
