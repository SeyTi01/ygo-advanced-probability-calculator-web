using YGOProbabilityCalculatorBlazor.Localization;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class LinkAppleEffect : ICardEffectDefinition {
    public int CanonicalCardId => 7925734;
    public string Name => "Link Apple";
    public int Revision => 1;
    public DrawEffectPlan Compile() => DrawEffectPlan.Draw(1, true);
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => UiText.English("Effects.7925734.Summary");
    public string Limitations => UiText.English("Effects.7925734.Limitations");
}
