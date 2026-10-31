using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class ContractWithDonThousandEffect : CardEffectDefinition {
    public override int CanonicalCardId => 56673480;
    public override string Name => "Contract with Don Thousand";
    public override int Revision => 1;
    public override DrawEffectPlan Compile() => DrawEffectPlan.Draw(1, true);
    public override EffectSupport Support => EffectSupport.ModeledWithinRules;
}
