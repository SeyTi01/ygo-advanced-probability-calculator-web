using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class LinkAppleEffect : ICardEffectDefinition {
    public int CanonicalCardId => 7925734;
    public string Name => "Link Apple";
    public int Revision => 1;
    public DrawEffectPlan Compile() => DrawEffectPlan.Draw(1, true);
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => "Draw 1; once per turn.";
    public string Limitations => "Assumes the Extra Deck reveal allows drawing.";
}
