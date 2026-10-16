using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class FairyTailBallEffect : ICardEffectDefinition {
    public int CanonicalCardId => 56725612;
    public string Name => "Fairy Tail Ball";
    public int Revision => 1;
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => "Search a Fairy Tail monster or Fairy Tail Tales";
    public string Limitations => "Models the Deck search; the banishment option and other Field Spell effects are outside the opening-hand model.";

    public DrawEffectPlan Compile() => new(null, true, 0, CardSelector.Any, 0, 0, CardSelector.Any,
        Search: new(CardSelector.AnyOfSelectors([
            CardSelector.AllOf(["metadata:kind:monster", "metadata:archetype:fairy%20tail"]),
            CardSelector.CatalogIdentity(91957038)
        ])));
}
