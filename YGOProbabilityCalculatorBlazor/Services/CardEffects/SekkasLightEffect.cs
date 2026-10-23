using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class SekkasLightEffect : ICardEffectDefinition {
    public int CanonicalCardId => 24940422;
    public string Name => "Sekka's Light";
    public int Revision => 1;
    public DrawEffectPlan Compile() => DrawEffectPlan.Draw(2, true);
    public EffectSupport Support => EffectSupport.Partial;
    public string Summary => "Draw 2; once per turn.";
    public string Limitations => "Assumes no Spells/Traps in the GY; its other Spell/Trap restriction and GY redraw effect are not modeled.";
}
