using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class SpellPowerMasteryEffect : CardEffectDefinition {
    public override int CanonicalCardId => 38943357;
    public override string Name => "Spell Power Mastery";
    public override int Revision => 1;
    public override EffectSupport Support => EffectSupport.ModeledWithinRules;
    public override DrawEffectPlan Compile() => DrawEffectPlan.SearchCards(new(new(CardSelectorKind.Category, ["metadata:archetype:endymion"])), true);
}
