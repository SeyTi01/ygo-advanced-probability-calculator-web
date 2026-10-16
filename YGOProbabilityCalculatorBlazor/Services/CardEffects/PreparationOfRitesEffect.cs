using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class PreparationOfRitesEffect : ICardEffectDefinition {
    private static readonly CardSelector LevelOneToSevenRitualMonster = CardSelector.AnyOfSelectors(
        Enumerable.Range(1, 7).Select(level => CardSelector.AllOf([
            "metadata:kind:monster", "metadata:monster-type:ritual", $"metadata:level:{level}"
        ])));

    public int CanonicalCardId => 96729612;
    public string Name => "Preparation of Rites";
    public int Revision => 1;
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => "Search 1 Level 1–7 Ritual monster";
    public DrawEffectPlan Compile() => new(null, false, 0, CardSelector.Any, 0, 0, CardSelector.Any,
        Search: new(LevelOneToSevenRitualMonster));
}
