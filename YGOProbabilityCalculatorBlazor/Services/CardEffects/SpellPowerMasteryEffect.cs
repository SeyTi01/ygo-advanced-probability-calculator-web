using YGOProbabilityCalculatorBlazor.Localization;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class SpellPowerMasteryEffect : ICardEffectDefinition {
    public int CanonicalCardId => 38943357;
    public string Name => "Spell Power Mastery";
    public int Revision => 1;
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => UiText.English("Effects.38943357.Summary");

    public DrawEffectPlan Compile() => new(null, true, 0, CardSelector.Any, 0, 0, CardSelector.Any,
        Search: new(new(CardSelectorKind.Category, ["metadata:archetype:endymion"])));
}
