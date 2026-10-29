using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class PreparationOfRitesEffect : CardEffectDefinition {
    private static readonly CardSelector LevelOneToSevenRitualMonster = CardSelector.AllOfSelectors([
        CardSelector.For(EffectProperties.Property("Ritual Monster", "monster-type:ritual")),
        new NumericPropertyRange(NumericPropertyKind.Level, 1, 7).ToSelector()
    ]);

    public override int CanonicalCardId => 96729612;
    public override string Name => "Preparation of Rites";
    public override int Revision => 4;
    public override EffectSupport Support => EffectSupport.ModeledWithinRules;
    public override string Limitations => ResourceText("Limitations");

    public override DrawEffectPlan Compile() => DrawEffectPlan.SearchCards(new(LevelOneToSevenRitualMonster), false);
}
