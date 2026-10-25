using YGOProbabilityCalculatorBlazor.Localization;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class FiendsmithEngraverEffect : ICardEffectDefinition {
    private static readonly CardSelector FiendsmithSpellOrTrap = CardSelector.AllOfSelectors([
        CardSelector.For(EffectProperties.Property("Archetype: Fiendsmith", "archetype:fiendsmith")),
        CardSelector.AnyOf(["metadata:kind:spell", "metadata:kind:trap"])
    ]);

    public int CanonicalCardId => 60764609;
    public string Name => "Fiendsmith Engraver";
    public int Revision => 2;
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => UiText.English("Effects.60764609.Summary");
    public DrawEffectPlan Compile() => new(null, true, 0, CardSelector.Any, 0, 0, CardSelector.Any,
        Search: new(FiendsmithSpellOrTrap));
}
