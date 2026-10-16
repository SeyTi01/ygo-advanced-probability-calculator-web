using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class BonfireEffect : ICardEffectDefinition {
    private static readonly CardSelector LevelOneToFourPyro = CardSelector.AnyOfSelectors(
        Enumerable.Range(1, 4).Select(level => CardSelector.AllOf([
            "metadata:kind:monster", "metadata:monster-race:pyro", $"metadata:level:{level}"
        ])));

    public int CanonicalCardId => 85106525;
    public string Name => "Bonfire";
    public int Revision => 1;
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => "Search 1 Level 1–4 Pyro monster";
    public DrawEffectPlan Compile() => new(null, true, 0, CardSelector.Any, 0, 0, CardSelector.Any,
        Search: new(LevelOneToFourPyro));
}
