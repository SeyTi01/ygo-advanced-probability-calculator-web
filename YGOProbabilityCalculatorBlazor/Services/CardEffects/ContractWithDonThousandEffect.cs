using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class ContractWithDonThousandEffect : ICardEffectDefinition {
    public int CanonicalCardId => 56673480;
    public string Name => "Contract with Don Thousand";
    public int Revision => 1;
    public DrawEffectPlan Compile() => DrawEffectPlan.Draw(1, true);
    public EffectSupport Support => EffectSupport.Partial;
    public string Summary => "Draw 1; once per turn.";
    public string Limitations => "Life points, opponent draw, reveals and restrictions are not modeled.";
}
