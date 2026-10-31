using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class TradeInEffect : CardEffectDefinition {
    public override IReadOnlyList<CategoryBase> FilterProperties => [LevelEight];
    public override int CanonicalCardId => 38120068;
    public override string Name => "Trade-In";
    public override int Revision => 2;
    public override EffectSupport Support => EffectSupport.ModeledWithinRules;
    public override DrawEffectPlan Compile() => DrawEffectPlan.Draw(2, false) with {
        HandCost = 1, HandCostSelector = new NumericPropertyRange(NumericPropertyKind.Level, 8, 8).ToSelector()
    };

    private static readonly CategoryBase LevelEight = EffectProperties.Property("Level 8", "level:8");
}
