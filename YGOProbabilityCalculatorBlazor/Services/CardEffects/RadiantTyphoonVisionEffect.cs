using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class RadiantTyphoonVisionEffect : ICardEffectDefinition {
    private static readonly CategoryBase QuickPlay = EffectProperties.Property("Quick-Play Spell", "spell-type:quick-play");
    private static readonly CategoryBase RadiantTyphoon = EffectProperties.Property("Archetype: Radiant Typhoon", "archetype:radiant%20typhoon");

    public IReadOnlyList<CategoryBase> FilterProperties => [QuickPlay, RadiantTyphoon];
    public int CanonicalCardId => 20508881;
    public string Name => "Radiant Typhoon Vision";
    public int Revision => 1;
    public DrawEffectPlan Compile() => DrawEffectPlan.Draw(2, true) with {
        RemovalCount = 1,
        RemovalSelector = CardSelector.AnyOf([QuickPlay.Identity, RadiantTyphoon.Identity])
    };
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => "Draw 2, then remove a Quick-Play spell or Radiant Typhoon card; once per turn.";
}
