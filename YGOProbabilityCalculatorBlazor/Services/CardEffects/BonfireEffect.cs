using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class BonfireEffect : ICardEffectDefinition {
    private static readonly CardSelector LevelOneToFourPyro = CardSelector.AllOfSelectors([
        CardSelector.For(EffectProperties.Property("Pyro", "monster-race:pyro")),
        new NumericPropertyRange(NumericPropertyKind.Level, 1, 4).ToSelector()
    ]);

    public int CanonicalCardId => 85106525;
    public string Name => "Bonfire";
    public int Revision => 4;
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => "Search 1 Level 1–4 Pyro monster";
    public DrawEffectPlan Compile() => new(null, true, 0, CardSelector.Any, 0, 0, CardSelector.Any,
        Search: new(LevelOneToFourPyro));
}
