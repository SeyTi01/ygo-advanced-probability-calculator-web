using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class UpstartGoblinEffect : ICardEffectDefinition {
    public int CanonicalCardId => 70368879;
    public string Name => "Upstart Goblin";
    public int Revision => 1;
    public DrawEffectPlan Compile() => DrawEffectPlan.Draw(1, false);
    public EffectSupport Support => EffectSupport.Partial;
    public string Summary => "Draw 1.";
    public string Limitations => "Opponent life-point gain is not modeled.";
}
