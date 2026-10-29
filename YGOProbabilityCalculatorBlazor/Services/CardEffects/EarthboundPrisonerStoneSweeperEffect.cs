using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class EarthboundPrisonerStoneSweeperEffect : CardEffectDefinition {
    private static readonly CardSelector LevelOneToThreeFiendTuner = CardSelector.AllOfSelectors([
        CardSelector.AllOf(["metadata:monster-trait:tuner", "metadata:monster-race:fiend"]),
        new NumericPropertyRange(NumericPropertyKind.Level, 1, 3).ToSelector()
    ]);

    public override int CanonicalCardId => 72323266;
    public override string Name => "Earthbound Prisoner Stone Sweeper";
    public override int Revision => 4;
    public override EffectSupport Support => EffectSupport.ModeledWithinRules;

    public override DrawEffectPlan Compile() => DrawEffectPlan.SearchCards(new(LevelOneToThreeFiendTuner), true);
}
