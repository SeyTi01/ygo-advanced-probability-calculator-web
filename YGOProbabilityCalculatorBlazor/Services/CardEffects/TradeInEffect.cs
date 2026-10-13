using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class TradeInEffect : ICardEffectDefinition {
    private static readonly CategoryBase LevelEight = EffectProperties.Property("Level 8", "level:8");

    public IReadOnlyList<CategoryBase> FilterProperties => [LevelEight];
    public int CanonicalCardId => 38120068;
    public string Name => "Trade-In";
    public int Revision => 1;
    public DrawEffectPlan Compile() => DrawEffectPlan.Draw(2, false) with {
        HandCost = 1, HandCostSelector = CardSelector.For(LevelEight)
    };
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => "Remove a Level 8 card from hand before drawing 2.";
}
