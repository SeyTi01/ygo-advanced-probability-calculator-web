using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class IntoTheVoidEffect : ICardEffectDefinition {
    public int CanonicalCardId => 93946239;
    public string Name => "Into the Void";
    public int Revision => 1;
    public DrawEffectPlan Compile() => DrawEffectPlan.Draw(1, false);
    public EffectSupport Support => EffectSupport.Partial;
    public string Summary => "Draw 1.";
    public string Limitations => "Minimum hand size and End Phase discard are not modeled.";
}
