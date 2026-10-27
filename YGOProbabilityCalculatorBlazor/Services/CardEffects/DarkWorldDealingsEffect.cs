using YGOProbabilityCalculatorBlazor.Localization;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class DarkWorldDealingsEffect : ICardEffectDefinition {
    public int CanonicalCardId => 74117290;
    public string Name => "Dark World Dealings";
    public int Revision => 1;
    public DrawEffectPlan Compile() => DrawEffectPlan.Draw(1, false) with { RemovalCount = 1 };
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => UiText.English("Effects.74117290.Summary");
}
