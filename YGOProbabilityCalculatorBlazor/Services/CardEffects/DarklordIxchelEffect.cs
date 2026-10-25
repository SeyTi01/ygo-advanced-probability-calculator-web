using YGOProbabilityCalculatorBlazor.Localization;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class DarklordIxchelEffect : ICardEffectDefinition {
    private static readonly CategoryBase Darklord = EffectProperties.Property("Archetype: Darklord", "archetype:darklord");

    public int CanonicalCardId => 52840267;
    public string Name => "Darklord Ixchel";
    public int Revision => 1;
    public IReadOnlyList<CategoryBase> FilterProperties => [Darklord];
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => UiText.English("Effects.52840267.Summary");

    public DrawEffectPlan Compile() => DrawEffectPlan.Draw(2, true) with {
        HandCost = 1,
        HandCostSelector = CardSelector.Objective(CardSelector.For(Darklord))
    };
}
