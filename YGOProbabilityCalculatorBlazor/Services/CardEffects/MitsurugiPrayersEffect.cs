using YGOProbabilityCalculatorBlazor.Localization;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class MitsurugiPrayersEffect : ICardEffectDefinition {
    public int CanonicalCardId => 45171524;
    public string Name => "Mitsurugi Prayers";
    public int Revision => 1;
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => UiText.English("Effects.45171524.Summary");
    public string Limitations => UiText.English("Effects.45171524.Limitations");

    public DrawEffectPlan Compile() => new(null, true, 0, CardSelector.Any, 0, 0, CardSelector.Any,
        Search: new(CardSelector.AllOf(["metadata:kind:monster", "metadata:archetype:mitsurugi"])));
}
