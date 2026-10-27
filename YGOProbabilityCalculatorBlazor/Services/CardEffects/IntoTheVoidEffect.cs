using YGOProbabilityCalculatorBlazor.Localization;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class IntoTheVoidEffect : ICardEffectDefinition {
    public int CanonicalCardId => 93946239;
    public string Name => "Into the Void";
    public int Revision => 2;
    public DrawEffectPlan Compile() => DrawEffectPlan.Draw(1, false) with { MinimumHandSize = 3 };
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => UiText.English("Effects.93946239.Summary");
}
