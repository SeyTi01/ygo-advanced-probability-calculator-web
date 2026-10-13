using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class AllureOfDarknessEffect : ICardEffectDefinition {
    private static readonly CategoryBase Dark = EffectProperties.Property("Attribute: DARK", "attribute:dark");

    public IReadOnlyList<CategoryBase> FilterProperties => [Dark];
    public int CanonicalCardId => 1475311;
    public string Name => "Allure of Darkness";
    public int Revision => 1;
    public DrawEffectPlan Compile() => DrawEffectPlan.Draw(2, false) with {
        RemovalCount = 1, RemovalSelector = CardSelector.For(Dark)
    };
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => "Draw 2, then remove a DARK card from hand.";
}
