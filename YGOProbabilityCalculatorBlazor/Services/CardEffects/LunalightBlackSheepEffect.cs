using YGOProbabilityCalculatorBlazor.Localization;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class LunalightBlackSheepEffect : ICardEffectDefinition {
    public int CanonicalCardId => 11317977;
    public string Name => "Lunalight Black Sheep";
    public int Revision => 1;
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => UiText.English("Effects.11317977.Summary");
    public string Limitations => UiText.English("Effects.11317977.Limitations");

    public DrawEffectPlan Compile() => new(null, false, 0, CardSelector.Any, 0, 0, CardSelector.Any,
        Search: new(CardSelector.CatalogName("Polymerization")));
}
