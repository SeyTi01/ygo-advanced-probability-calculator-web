using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class AllureOfDarknessEffect : CardEffectDefinition {
    private static readonly CategoryBase Dark = EffectProperties.Property("Attribute: DARK", "attribute:dark");
    public override IReadOnlyList<CategoryBase> FilterProperties => [Dark];
    public override int CanonicalCardId => 1475311;
    public override string Name => "Allure of Darkness";
    public override int Revision => 1;
    public override EffectSupport Support => EffectSupport.ModeledWithinRules;
    public override DrawEffectPlan Compile() => DrawEffectPlan.Draw(2, false) with {
        RemovalCount = 1, RemovalSelector = CardSelector.For(Dark)
    };
}
