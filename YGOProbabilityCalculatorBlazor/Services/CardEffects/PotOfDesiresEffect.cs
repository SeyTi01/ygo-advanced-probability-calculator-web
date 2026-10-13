using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class PotOfDesiresEffect : ICardEffectDefinition {
    public int CanonicalCardId => 35261759;
    public string Name => "Pot of Desires";
    public int Revision => 1;
    public DrawEffectPlan Compile() => DrawEffectPlan.Draw(2, true) with { HiddenDeckCost = 10 };
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => "Banish 10 unseen deck cards, then draw 2; once per turn. Requires 12 cards left.";
}
