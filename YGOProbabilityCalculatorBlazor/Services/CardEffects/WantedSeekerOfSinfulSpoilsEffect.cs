using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class WantedSeekerOfSinfulSpoilsEffect : ICardEffectDefinition {
    public int CanonicalCardId => 80845034;
    public string Name => "WANTED: Seeker of Sinful Spoils";
    public int Revision => 1;
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => "Search 1 Diabellstar monster from the Main Deck";
    public string Limitations => "Models the Main Deck search only; its GY add option and separate GY draw effect are outside scope.";

    public DrawEffectPlan Compile() => new(null, true, 0, CardSelector.Any, 0, 0, CardSelector.Any,
        Search: new(CardSelector.AllOf(["metadata:kind:monster", "metadata:archetype:diabellstar"])));
}
