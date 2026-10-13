using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class RadiantTyphoonVisionEffect : ICardEffectDefinition {
    public int CanonicalCardId => 20508881;
    public string Name => "Radiant Typhoon Vision";
    public int Revision => 1;
    public DrawEffectPlan Compile() => DrawEffectPlan.Draw(2, true) with {
        RemovalCount = 1,
        RemovalSelector = CardSelector.AnyOf(["metadata:spell-type:quick-play", "metadata:archetype:radiant%20typhoon"])
    };
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => "Draw 2, then remove a Quick-Play spell or Radiant Typhoon card; once per turn.";
}
