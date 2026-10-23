using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class FireFormationTenkiEffect : ICardEffectDefinition {
    private static readonly CardSelector LevelOneToFourBeastWarrior = CardSelector.AllOfSelectors([
        CardSelector.For(EffectProperties.Property("Beast-Warrior", "monster-race:beast-warrior")),
        new NumericPropertyRange(NumericPropertyKind.Level, 1, 4).ToSelector()
    ]);

    public int CanonicalCardId => 57103969;
    public string Name => "Fire Formation - Tenki";
    public int Revision => 4;
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => "Search 1 Level 1–4 Beast-Warrior monster";
    public DrawEffectPlan Compile() => new(null, true, 0, CardSelector.Any, 0, 0, CardSelector.Any,
        Search: new(LevelOneToFourBeastWarrior));
}
