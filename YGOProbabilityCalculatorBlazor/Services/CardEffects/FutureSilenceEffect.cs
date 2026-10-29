using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class FutureSilenceEffect : CardEffectDefinition {
    public override int CanonicalCardId => 78679226;
    public override string Name => "Future Silence";
    public override int Revision => 1;
    public override EffectSupport Support => EffectSupport.ModeledWithinRules;
    public override string Limitations => ResourceText("Limitations");

    public override DrawEffectPlan Compile() => DrawEffectPlan.SearchCards(
        new(CardSelector.AllOfSelectors([
            CardSelector.AllOf(["metadata:kind:monster"]),
            CardSelector.Mentions("Shining Sarcophagus")
        ])),
        true
    );
}
