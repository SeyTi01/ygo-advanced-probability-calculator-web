using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class FiendsmithTractEffect : CardEffectDefinition {
    public override int CanonicalCardId => 98567237;
    public override string Name => "Fiendsmith's Tract";
    public override int Revision => 2;
    public override EffectSupport Support => EffectSupport.ModeledWithinRules;
    public override DrawEffectPlan Compile() => DrawEffectPlan.SearchCards(
        new(CardSelector.AllOf(["metadata:attribute:light", "metadata:monster-race:fiend"])),
        true
    ) with { RemovalCount = 1 };
}
