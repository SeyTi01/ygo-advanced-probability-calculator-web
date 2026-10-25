using YGOProbabilityCalculatorBlazor.Localization;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class NightTrainBlueTravelerEffect : ICardEffectDefinition {
    private static readonly CardSelector Targets = CardSelector.AnyOfSelectors([
        CardSelector.CatalogIdentity(60879050),
        CardSelector.CatalogIdentity(76136345)
    ]);

    public int CanonicalCardId => 81101309;
    public string Name => "Night Train Blue Traveler";
    public int Revision => 1;
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => UiText.English("Effects.81101309.Summary");
    public string Limitations => UiText.English("Effects.81101309.Limitations");

    public DrawEffectPlan Compile() => new(null, true, 0, CardSelector.Any, 0, 0, CardSelector.Any,
        Search: new(Targets));
}
