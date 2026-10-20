using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class EarthboundPrisonerStoneSweeperEffect : ICardEffectDefinition {
    private static readonly CardSelector LevelOneToThreeFiendTuner = CardSelector.AllOfSelectors([
        CardSelector.AllOf(["metadata:kind:monster", "metadata:monster-trait:tuner", "metadata:monster-race:fiend"]),
        CardSelector.AnyOf(Enumerable.Range(1, 3).Select(level => $"metadata:level:{level}"))
    ]);

    public int CanonicalCardId => 72323266;
    public string Name => "Earthbound Prisoner Stone Sweeper";
    public int Revision => 2;
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => "Search 1 Level 1–3 Fiend Tuner monster";
    public DrawEffectPlan Compile() => new(null, true, 0, CardSelector.Any, 0, 0, CardSelector.Any,
        Search: new(LevelOneToThreeFiendTuner));
}
