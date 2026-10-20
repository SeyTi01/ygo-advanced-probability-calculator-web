using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class ReinforcementOfTheArmyEffect : ICardEffectDefinition {
    public int CanonicalCardId => 32807846;
    public string Name => "Reinforcement of the Army";
    public int Revision => 3;
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => "Search 1 Level 1–4 Warrior monster";
    public DrawEffectPlan Compile() => new(null, false, 0, CardSelector.Any, 0, 0, CardSelector.Any,
        Search: new(CardSelector.AllOfSelectors([
            CardSelector.For(EffectProperties.Property("Warrior", "monster-race:warrior")),
            CardSelector.AnyOf(Enumerable.Range(1, 4).Select(level => $"metadata:level:{level}"))
        ])));
}
