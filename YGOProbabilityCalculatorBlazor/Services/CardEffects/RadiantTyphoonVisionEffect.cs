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
    public string Summary => "Draw 2, then discard 1 eligible Quick-Play Spell or Radiant Typhoon card; once per turn.";
    public string Limitations => "The alternative that adds Mystical Space Typhoon from the Deck or GY to the hand is not modeled. If no eligible discard remains after drawing, that outcome is counted as unsuccessful and stays in the original probability denominator (FailBranch); the engine does not explicitly discard the entire remaining hand. This has the same success result for objectives requiring at least one final-hand card, but can differ for absence-only or exclusion objectives that accept an empty hand.";
}
