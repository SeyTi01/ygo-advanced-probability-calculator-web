using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class KetuDracotailEffect : CardEffectDefinition {
    public override int CanonicalCardId => 6153210;
    public override string Name => "Ketu Dracotail";
    public override int Revision => 1;
    public override EffectSupport Support => EffectSupport.ModeledWithinRules;
    public override string Limitations => ResourceText("Limitations");

    public override DrawEffectPlan Compile() => DrawEffectPlan.SearchCards(
        new(CardSelector.AllOf(["metadata:kind:monster", "metadata:archetype:dracotail"])),
        true
    );
}
