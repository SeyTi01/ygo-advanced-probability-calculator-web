using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class KetuDracotailEffect : ICardEffectDefinition {
    public int CanonicalCardId => 6153210;
    public string Name => "Ketu Dracotail";
    public int Revision => 1;
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => "Search 1 Dracotail monster";
    public string Limitations => "The opponent-dependent Fusion Summon is outside the opening-hand search model.";

    public DrawEffectPlan Compile() => new(null, true, 0, CardSelector.Any, 0, 0, CardSelector.Any,
        Search: new(CardSelector.AllOf(["metadata:kind:monster", "metadata:archetype:dracotail"])));
}
