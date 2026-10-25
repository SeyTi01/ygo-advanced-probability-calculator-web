using YGOProbabilityCalculatorBlazor.Localization;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class EmpireOfEndymionEffect : ICardEffectDefinition {
    public int CanonicalCardId => 34041788;
    public string Name => "Empire of Endymion";
    public int Revision => 1;
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => UiText.English("Effects.34041788.Summary");
    public string Limitations => UiText.English("Effects.34041788.Limitations");

    public DrawEffectPlan Compile() => new(null, true, 0, CardSelector.Any, 0, 0, CardSelector.Any,
        Search: new(CardSelector.AnyOfSelectors([
            CardSelector.CatalogIdentity(96228804),
            CardSelector.AllOfSelectors([
                CardSelector.AllOf(["metadata:kind:monster"]),
                CardSelector.Mentions("Regulus, the Prince of Endymion")
            ])
        ])));
}
