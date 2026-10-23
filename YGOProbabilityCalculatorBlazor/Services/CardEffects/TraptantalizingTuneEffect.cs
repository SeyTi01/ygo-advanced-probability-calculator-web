using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class TraptantalizingTuneEffect : ICardEffectDefinition {
    private static readonly CardSelector LevelFourInsectOrPlant = CardSelector.AllOfSelectors([
        new NumericPropertyRange(NumericPropertyKind.Level, 4, 4).ToSelector(),
        CardSelector.AnyOf([
            "metadata:monster-race:insect",
            "metadata:monster-race:plant"
        ])
    ]);
    private static readonly CardSelector NormalTrap = CardSelector.For(EffectProperties.Property("Normal Trap", "trap-type:normal"));

    public int CanonicalCardId => 48805472;
    public string Name => "Traptantalizing Tune";
    public int Revision => 3;
    public IReadOnlyList<CategoryBase> FilterProperties => [
        EffectProperties.Property("Level 4", "level:4"),
        EffectProperties.Property("Insect", "monster-race:insect"),
        EffectProperties.Property("Plant", "monster-race:plant"),
        EffectProperties.Property("Trap", "kind:trap"),
        EffectProperties.Property("Normal Trap", "trap-type:normal")
    ];
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => "Discard 1 Level 4 Insect or Plant monster, or 1 Normal Trap, from hand to draw 2; once per turn.";

    public DrawEffectPlan Compile() => DrawEffectPlan.Draw(2, true) with {
        HandCost = 1,
        HandCostSelector = CardSelector.Objective(CardSelector.AnyOfSelectors([
            LevelFourInsectOrPlant,
            NormalTrap
        ]))
    };
}
