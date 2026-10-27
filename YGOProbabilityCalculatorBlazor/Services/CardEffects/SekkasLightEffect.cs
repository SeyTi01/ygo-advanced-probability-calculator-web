using YGOProbabilityCalculatorBlazor.Localization;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class SekkasLightEffect : ICardEffectDefinition {
    public int CanonicalCardId => 24940422;
    public string Name => "Sekka's Light";
    public int Revision => 1;
    public DrawEffectPlan Compile() => DrawEffectPlan.Draw(2, true);
    public EffectSupport Support => EffectSupport.Partial;
    public string Summary => UiText.English("Effects.24940422.Summary");
    public string Limitations => UiText.English("Effects.24940422.Limitations");
}
