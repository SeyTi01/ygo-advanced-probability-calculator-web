using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class WantedSeekerOfSinfulSpoilsEffect : CardEffectDefinition {
    public override int CanonicalCardId => 80845034;
    public override string Name => "WANTED: Seeker of Sinful Spoils";
    public override int Revision => 1;
    public override EffectSupport Support => EffectSupport.ModeledWithinRules;
    public override string Limitations => ResourceText("Limitations");
    public override DrawEffectPlan Compile() => DrawEffectPlan.SearchCards(
        new(CardSelector.AllOf(["metadata:kind:monster", "metadata:archetype:diabellstar"])),
        true
    );
}
