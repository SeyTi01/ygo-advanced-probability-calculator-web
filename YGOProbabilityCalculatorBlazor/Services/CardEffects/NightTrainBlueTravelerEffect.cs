using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class NightTrainBlueTravelerEffect : CardEffectDefinition {
    public override int CanonicalCardId => 81101309;
    public override string Name => "Night Train Blue Traveler";
    public override int Revision => 1;
    public override EffectSupport Support => EffectSupport.ModeledWithinRules;
    public override string Limitations => ResourceText("Limitations");
    public override DrawEffectPlan Compile() => DrawEffectPlan.SearchCards(new(Targets), true);

    private static readonly CardSelector Targets = CardSelector.AnyOfSelectors([
        CardSelector.CatalogIdentity(60879050),
        CardSelector.CatalogIdentity(76136345)
    ]);
}
