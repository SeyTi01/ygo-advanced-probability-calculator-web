using YGOProbabilityCalculatorBlazor.Localization;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class ReinforcementOfTheArmyEffect : ICardEffectDefinition {
    public int CanonicalCardId => 32807846;
    public string Name => "Reinforcement of the Army";
    public int Revision => 4;
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => UiText.English("Effects.32807846.Summary");
    public DrawEffectPlan Compile() => new(null, false, 0, CardSelector.Any, 0, 0, CardSelector.Any,
        Search: new(CardSelector.AllOfSelectors([
            CardSelector.For(EffectProperties.Property("Warrior", "monster-race:warrior")),
            new NumericPropertyRange(NumericPropertyKind.Level, 1, 4).ToSelector()
        ])));
}
