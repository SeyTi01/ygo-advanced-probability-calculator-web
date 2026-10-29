using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class LunalightBlackSheepEffect : CardEffectDefinition {
    public override int CanonicalCardId => 11317977;
    public override string Name => "Lunalight Black Sheep";
    public override int Revision => 1;
    public override EffectSupport Support => EffectSupport.ModeledWithinRules;
    public override string Limitations => ResourceText("Limitations");

    public override DrawEffectPlan Compile() => DrawEffectPlan.SearchCards(new(CardSelector.CatalogName("Polymerization")), false);
}
