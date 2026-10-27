using YGOProbabilityCalculatorBlazor.Localization;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class PotOfDesiresEffect : ICardEffectDefinition {
    public int CanonicalCardId => 35261759;
    public string Name => "Pot of Desires";
    public int Revision => 1;
    public DrawEffectPlan Compile() => DrawEffectPlan.Draw(2, true) with { HiddenDeckCost = 10 };
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => UiText.English("Effects.35261759.Summary");
    public string Limitations => UiText.English("Effects.35261759.Limitations");
}
