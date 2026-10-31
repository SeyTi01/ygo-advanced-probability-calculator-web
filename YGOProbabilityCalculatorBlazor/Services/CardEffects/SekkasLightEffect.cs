using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class SekkasLightEffect : CardEffectDefinition {
    public override int CanonicalCardId => 24940422;
    public override string Name => "Sekka's Light";
    public override int Revision => 1;
    public override DrawEffectPlan Compile() => DrawEffectPlan.Draw(2, true);
    public override EffectSupport Support => EffectSupport.Partial;
    public override string Limitations => ResourceText("Limitations");
}
