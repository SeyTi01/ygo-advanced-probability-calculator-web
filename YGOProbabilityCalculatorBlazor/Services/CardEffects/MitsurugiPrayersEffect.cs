using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class MitsurugiPrayersEffect : ICardEffectDefinition {
    public int CanonicalCardId => 45171524;
    public string Name => "Mitsurugi Prayers";
    public int Revision => 1;
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => "Search 1 Mitsurugi monster";
    public string Limitations => "Models the search mode without a Tribute; the alternate damage and Special Summon mode is outside scope.";

    public DrawEffectPlan Compile() => new(null, true, 0, CardSelector.Any, 0, 0, CardSelector.Any,
        Search: new(CardSelector.AllOf(["metadata:kind:monster", "metadata:archetype:mitsurugi"])));
}
