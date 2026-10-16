using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class FiendsmithTractEffect : ICardEffectDefinition {
    public int CanonicalCardId => 98567237;
    public string Name => "Fiendsmith's Tract";
    public int Revision => 1;
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => "Search a LIGHT Fiend, then discard 1 card";
    public DrawEffectPlan Compile() => new(null, true, 0, CardSelector.Any, 0, 1, CardSelector.Any,
        Search: new(CardSelector.AllOf(["metadata:attribute:light", "metadata:monster-race:fiend", "metadata:kind:monster"])));
}
