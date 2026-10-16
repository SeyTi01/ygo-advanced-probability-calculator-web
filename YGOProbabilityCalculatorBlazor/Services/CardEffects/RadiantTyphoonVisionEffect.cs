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
    public string Summary => "Draw 2, then discard one Quick-Play Spell or Radiant Typhoon card; once per turn.";
    public string Limitations => "Only the Draw 2, then discard 1 Quick-Play Spell or Radiant Typhoon card option is modeled. The alternative that adds Mystical Space Typhoon from the Deck to the hand is not modeled. If no eligible discard remains after drawing, the effect requires discarding your entire hand; that fallback is not modeled.";
}
