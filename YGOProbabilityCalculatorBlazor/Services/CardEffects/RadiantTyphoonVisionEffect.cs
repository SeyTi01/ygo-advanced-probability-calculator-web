using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class RadiantTyphoonVisionEffect : CardEffectDefinition {
    private static readonly CategoryBase QuickPlay = EffectProperties.Property("Quick-Play Spell", "spell-type:quick-play");
    private static readonly CategoryBase RadiantTyphoon = EffectProperties.Property("Archetype: Radiant Typhoon", "archetype:radiant%20typhoon");

    public override IReadOnlyList<CategoryBase> FilterProperties => [QuickPlay, RadiantTyphoon];
    public override int CanonicalCardId => 20508881;
    public override string Name => "Radiant Typhoon Vision";
    public override int Revision => 2;

    public override DrawEffectPlan Compile() => DrawEffectPlan.Draw(2, true) with {
        RemovalCount = 1,
        RemovalSelector = CardSelector.AnyOf([QuickPlay.Identity, RadiantTyphoon.Identity])
    };
    public override EffectSupport Support => EffectSupport.Partial;
    public override string Limitations => ResourceText("Limitations");
}
