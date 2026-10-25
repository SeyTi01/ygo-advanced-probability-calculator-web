using YGOProbabilityCalculatorBlazor.Localization;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class UpstartGoblinEffect : ICardEffectDefinition {
    public int CanonicalCardId => 70368879;
    public string Name => "Upstart Goblin";
    public int Revision => 1;
    public DrawEffectPlan Compile() => DrawEffectPlan.Draw(1, false);
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => UiText.English("Effects.70368879.Summary");
}
