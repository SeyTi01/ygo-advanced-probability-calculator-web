using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class TradeInEffect : ICardEffectDefinition {
    public int CanonicalCardId => 38120068;
    public string Name => "Trade-In";
    public int Revision => 1;
    public DrawEffectPlan Compile() => DrawEffectPlan.Draw(2, false) with {
        HandCost = 1, HandCostSelector = EffectProperties.Selector("level:8")
    };
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => "Remove a Level 8 card from hand before drawing 2.";
    public string Limitations => "Uses deterministic hand-cost selection; discard triggers and graveyard state are not modeled.";
}
