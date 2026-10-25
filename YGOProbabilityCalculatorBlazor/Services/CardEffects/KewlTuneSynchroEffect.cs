using YGOProbabilityCalculatorBlazor.Localization;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class KewlTuneSynchroEffect : ICardEffectDefinition {
    public int CanonicalCardId => 78058681;
    public string Name => "Kewl Tune Synchro";
    public int Revision => 1;
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => UiText.English("Effects.78058681.Summary");
    public DrawEffectPlan Compile() => new(null, false, 0, CardSelector.Any, 0, 0, CardSelector.Any,
        Search: new(CardSelector.AllOfSelectors([new(CardSelectorKind.Category, ["metadata:archetype:kewl%20tune"]), CardSelector.Not(CardSelector.CatalogIdentity(78058681))])), ActivationLimit: 2);
}
