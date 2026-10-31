using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class FireFormationTenkiEffect : CardEffectDefinition {
    public override int CanonicalCardId => 57103969;
    public override string Name => "Fire Formation - Tenki";
    public override int Revision => 4;
    public override EffectSupport Support => EffectSupport.ModeledWithinRules;
    public override DrawEffectPlan Compile() => DrawEffectPlan.SearchCards(new(LevelOneToFourBeastWarrior), true);

    private static readonly CardSelector LevelOneToFourBeastWarrior = CardSelector.AllOfSelectors([
        CardSelector.For(EffectProperties.Property("Beast-Warrior", "monster-race:beast-warrior")),
        new NumericPropertyRange(NumericPropertyKind.Level, 1, 4).ToSelector()
    ]);
}
