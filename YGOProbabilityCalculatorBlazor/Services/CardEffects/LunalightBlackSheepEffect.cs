using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class LunalightBlackSheepEffect : ICardEffectDefinition {
    public int CanonicalCardId => 11317977;
    public string Name => "Lunalight Black Sheep";
    public int Revision => 1;
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => "Discard this card to add Polymerization from the Deck; no once-per-turn limit.";
    public string Limitations => "Models the Polymerization search branch; the separate Graveyard recovery branch is outside scope.";

    public DrawEffectPlan Compile() => new(null, false, 0, CardSelector.Any, 0, 0, CardSelector.Any,
        Search: new(CardSelector.CatalogName("Polymerization")));
}
