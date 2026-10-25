using YGOProbabilityCalculatorBlazor.Localization;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class EarthboundPrisonerStoneSweeperEffect : ICardEffectDefinition {
    private static readonly CardSelector LevelOneToThreeFiendTuner = CardSelector.AllOfSelectors([
        CardSelector.AllOf(["metadata:monster-trait:tuner", "metadata:monster-race:fiend"]),
        new NumericPropertyRange(NumericPropertyKind.Level, 1, 3).ToSelector()
    ]);

    public int CanonicalCardId => 72323266;
    public string Name => "Earthbound Prisoner Stone Sweeper";
    public int Revision => 4;
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => UiText.English("Effects.72323266.Summary");
    public DrawEffectPlan Compile() => new(null, true, 0, CardSelector.Any, 0, 0, CardSelector.Any,
        Search: new(LevelOneToThreeFiendTuner));
}
