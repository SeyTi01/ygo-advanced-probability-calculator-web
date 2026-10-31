using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class BonfireEffect : CardEffectDefinition {
    private static readonly CardSelector LevelOneToFourPyro = CardSelector.AllOfSelectors([
        CardSelector.For(EffectProperties.Property("Pyro", "monster-race:pyro")),
        new NumericPropertyRange(NumericPropertyKind.Level, 1, 4).ToSelector()
    ]);

    public override int CanonicalCardId => 85106525;
    public override string Name => "Bonfire";
    public override int Revision => 4;
    public override EffectSupport Support => EffectSupport.ModeledWithinRules;
    public override DrawEffectPlan Compile() => DrawEffectPlan.SearchCards(new(LevelOneToFourPyro), true);
}
