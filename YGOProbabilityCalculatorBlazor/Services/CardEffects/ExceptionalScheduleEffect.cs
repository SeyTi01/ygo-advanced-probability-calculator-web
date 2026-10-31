using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class ExceptionalScheduleEffect : CardEffectDefinition {

    public override int CanonicalCardId => 52782439;
    public override string Name => "Exceptional Schedule";
    public override int Revision => 1;
    public override EffectSupport Support => EffectSupport.ModeledWithinRules;
    public override string Limitations => ResourceText("Limitations");
    public override DrawEffectPlan Compile() => DrawEffectPlan.SearchCards(new(Targets), true);

    private static readonly CardSelector Targets = CardSelector.AnyOfSelectors([
        CardSelector.CatalogIdentity(97520701),
        CardSelector.CatalogIdentity(25274141)
    ]);
}
