using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class KewlTuneSynchroEffect : CardEffectDefinition {
    public override int CanonicalCardId => 78058681;
    public override string Name => "Kewl Tune Synchro";
    public override int Revision => 1;
    public override EffectSupport Support => EffectSupport.ModeledWithinRules;
    public override DrawEffectPlan Compile() => DrawEffectPlan.SearchCards(
        new(CardSelector.AllOfSelectors([
            new(CardSelectorKind.Category, ["metadata:archetype:kewl%20tune"]),
            CardSelector.Not(CardSelector.CatalogIdentity(78058681))
        ])),
        false
    ) with { ActivationLimit = 2 };
}
