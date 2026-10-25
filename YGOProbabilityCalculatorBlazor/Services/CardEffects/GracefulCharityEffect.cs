using YGOProbabilityCalculatorBlazor.Localization;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class GracefulCharityEffect : ICardEffectDefinition {
    public int CanonicalCardId => 79571449;
    public string Name => "Graceful Charity";
    public int Revision => 1;
    public DrawEffectPlan Compile() => DrawEffectPlan.Draw(3, false) with { RemovalCount = 2 };
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => UiText.English("Effects.79571449.Summary");
}
