using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class ReinforcementOfTheArmyEffect : CardEffectDefinition {
    public override int CanonicalCardId => 32807846;
    public override string Name => "Reinforcement of the Army";
    public override int Revision => 4;
    public override EffectSupport Support => EffectSupport.ModeledWithinRules;

    public override DrawEffectPlan Compile() => DrawEffectPlan.SearchCards(
        new(CardSelector.AllOfSelectors([
            CardSelector.For(EffectProperties.Property("Warrior", "monster-race:warrior")),
            new NumericPropertyRange(NumericPropertyKind.Level, 1, 4).ToSelector()
        ])),
        false
    );
}
