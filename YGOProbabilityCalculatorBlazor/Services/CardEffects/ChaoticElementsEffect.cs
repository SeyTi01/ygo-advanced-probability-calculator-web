using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class ChaoticElementsEffect : CardEffectDefinition {
    public override int CanonicalCardId => 92221402;
    public override string Name => "Chaotic Elements";
    public override int Revision => 3;
    public override EffectSupport Support => EffectSupport.ModeledWithinRules;
    public override string Limitations => ResourceText("Limitations");

    public override DrawEffectPlan Compile() => DrawEffectPlan.SearchCards(
        new(CardSelector.AllOfSelectors([
            CardSelector.LevelAtLeast(5),
            CardSelector.AnyOf(["metadata:attribute:light", "metadata:attribute:dark"]),
            CardSelector.AnyOf(["metadata:monster-race:pyro", "metadata:monster-race:aqua"])
        ])),
        true
    );
}
