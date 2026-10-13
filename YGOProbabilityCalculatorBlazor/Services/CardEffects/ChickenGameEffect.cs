using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class ChickenGameEffect : ICardEffectDefinition {
    public int CanonicalCardId => 67616300;
    public string Name => "Chicken Game";
    public int Revision => 1;
    public DrawEffectPlan Compile() => DrawEffectPlan.Draw(1, false);
    public EffectSupport Support => EffectSupport.Partial;
    public string Summary => "Draw 1.";
    public string Limitations => "Life-point cost, field state and other options are not modeled.";
}
