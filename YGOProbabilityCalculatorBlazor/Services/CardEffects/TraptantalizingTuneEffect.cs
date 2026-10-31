using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class TraptantalizingTuneEffect : CardEffectDefinition {
    public override int CanonicalCardId => 48805472;
    public override string Name => "Traptantalizing Tune";
    public override int Revision => 3;
    public override IReadOnlyList<CategoryBase> FilterProperties => [
        EffectProperties.Property("Level 4", "level:4"),
        EffectProperties.Property("Insect", "monster-race:insect"),
        EffectProperties.Property("Plant", "monster-race:plant"),
        EffectProperties.Property("Trap", "kind:trap"),
        EffectProperties.Property("Normal Trap", "trap-type:normal")
    ];

    public override EffectSupport Support => EffectSupport.ModeledWithinRules;
    public override DrawEffectPlan Compile() => DrawEffectPlan.Draw(2, true) with {
        HandCost = 1,
        HandCostSelector = CardSelector.Objective(CardSelector.AnyOfSelectors([
            LevelFourInsectOrPlant,
            NormalTrap
        ]))
    };

    private static readonly CardSelector LevelFourInsectOrPlant = CardSelector.AllOfSelectors([
        new NumericPropertyRange(NumericPropertyKind.Level, 4, 4).ToSelector(),
        CardSelector.AnyOf([
            "metadata:monster-race:insect",
            "metadata:monster-race:plant"
        ])
    ]);

    private static readonly CardSelector NormalTrap = CardSelector.For(EffectProperties.Property("Normal Trap", "trap-type:normal"));
}
