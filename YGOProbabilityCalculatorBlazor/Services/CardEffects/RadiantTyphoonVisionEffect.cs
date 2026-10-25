using YGOProbabilityCalculatorBlazor.Localization;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class RadiantTyphoonVisionEffect : ICardEffectDefinition {
    private static readonly CategoryBase QuickPlay = EffectProperties.Property("Quick-Play Spell", "spell-type:quick-play");
    private static readonly CategoryBase RadiantTyphoon = EffectProperties.Property("Archetype: Radiant Typhoon", "archetype:radiant%20typhoon");

    public IReadOnlyList<CategoryBase> FilterProperties => [QuickPlay, RadiantTyphoon];
    public int CanonicalCardId => 20508881;
    public string Name => "Radiant Typhoon Vision";
    public int Revision => 2;
    public DrawEffectPlan Compile() => DrawEffectPlan.Draw(2, true) with {
        RemovalCount = 1,
        RemovalSelector = CardSelector.AnyOf([QuickPlay.Identity, RadiantTyphoon.Identity])
    };
    public EffectSupport Support => EffectSupport.Partial;
    public string Summary => UiText.English("Effects.20508881.Summary");
    public string Limitations => UiText.English("Effects.20508881.Limitations");
}
