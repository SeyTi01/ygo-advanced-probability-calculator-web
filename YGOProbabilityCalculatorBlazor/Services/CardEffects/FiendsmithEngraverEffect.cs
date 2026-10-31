using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class FiendsmithEngraverEffect : CardEffectDefinition {
    public override int CanonicalCardId => 60764609;
    public override string Name => "Fiendsmith Engraver";
    public override int Revision => 2;
    public override EffectSupport Support => EffectSupport.ModeledWithinRules;
    public override DrawEffectPlan Compile() => DrawEffectPlan.SearchCards(new(FiendsmithSpellOrTrap), true);

    private static readonly CardSelector FiendsmithSpellOrTrap = CardSelector.AllOfSelectors([
        CardSelector.For(EffectProperties.Property("Archetype: Fiendsmith", "archetype:fiendsmith")),
        CardSelector.AnyOf(["metadata:kind:spell", "metadata:kind:trap"])
    ]);
}
