using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class MitsurugiPrayersEffect : CardEffectDefinition {
    public override int CanonicalCardId => 45171524;
    public override string Name => "Mitsurugi Prayers";
    public override int Revision => 1;
    public override EffectSupport Support => EffectSupport.ModeledWithinRules;
    public override string Limitations => ResourceText("Limitations");

    public override DrawEffectPlan Compile() => DrawEffectPlan.SearchCards(
        new(CardSelector.AllOf(["metadata:kind:monster", "metadata:archetype:mitsurugi"])),
        true
    );
}
