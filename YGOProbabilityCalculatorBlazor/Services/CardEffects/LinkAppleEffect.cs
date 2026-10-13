using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class LinkAppleEffect : ICardEffectDefinition {
    public int CanonicalCardId => 7925734;
    public string Name => "Link Apple";
    public int Revision => 1;
    public DrawEffectPlan Compile() => DrawEffectPlan.Draw(1, true);
    public EffectSupport Support => EffectSupport.Partial;
    public string Summary => "Draw 1; once per turn.";
    public string Limitations => "Random Extra Deck outcome, summon alternative and reveal are not modeled; always draws one.";
}
