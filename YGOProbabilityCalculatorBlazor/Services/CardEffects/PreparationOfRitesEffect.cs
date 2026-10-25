using YGOProbabilityCalculatorBlazor.Localization;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class PreparationOfRitesEffect : ICardEffectDefinition {
    private static readonly CardSelector LevelOneToSevenRitualMonster = CardSelector.AllOfSelectors([
        CardSelector.For(EffectProperties.Property("Ritual Monster", "monster-type:ritual")),
        new NumericPropertyRange(NumericPropertyKind.Level, 1, 7).ToSelector()
    ]);

    public int CanonicalCardId => 96729612;
    public string Name => "Preparation of Rites";
    public int Revision => 4;
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => UiText.English("Effects.96729612.Summary");
    public string Limitations => UiText.English("Effects.96729612.Limitations");
    public DrawEffectPlan Compile() => new(null, false, 0, CardSelector.Any, 0, 0, CardSelector.Any,
        Search: new(LevelOneToSevenRitualMonster));
}
