using YGOProbabilityCalculatorBlazor.Localization;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class KetuDracotailEffect : ICardEffectDefinition {
    public int CanonicalCardId => 6153210;
    public string Name => "Ketu Dracotail";
    public int Revision => 1;
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => UiText.English("Effects.6153210.Summary");
    public string Limitations => UiText.English("Effects.6153210.Limitations");

    public DrawEffectPlan Compile() => new(null, true, 0, CardSelector.Any, 0, 0, CardSelector.Any,
        Search: new(CardSelector.AllOf(["metadata:kind:monster", "metadata:archetype:dracotail"])));
}
