using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class FiendsmithEngraverEffect : ICardEffectDefinition {
    private static readonly CardSelector FiendsmithSpellOrTrap = CardSelector.AnyOfSelectors([
        CardSelector.AllOf(["metadata:archetype:fiendsmith", "metadata:kind:spell"]),
        CardSelector.AllOf(["metadata:archetype:fiendsmith", "metadata:kind:trap"])
    ]);

    public int CanonicalCardId => 60764609;
    public string Name => "Fiendsmith Engraver";
    public int Revision => 1;
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => "Search 1 Fiendsmith Spell/Trap";
    public DrawEffectPlan Compile() => new(null, true, 0, CardSelector.Any, 0, 0, CardSelector.Any,
        Search: new(FiendsmithSpellOrTrap));
}
