using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class DarklordIxchelEffect : ICardEffectDefinition {
    private static readonly CategoryBase Darklord = EffectProperties.Property("Archetype: Darklord", "archetype:darklord");

    public int CanonicalCardId => 52840267;
    public string Name => "Darklord Ixchel";
    public int Revision => 1;
    public IReadOnlyList<CategoryBase> FilterProperties => [Darklord];
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => "Discard Ixchel and 1 other Darklord card from hand to draw 2; once per turn.";

    public DrawEffectPlan Compile() => DrawEffectPlan.Draw(2, true) with {
        HandCost = 1,
        HandCostSelector = CardSelector.Objective(CardSelector.For(Darklord))
    };
}
