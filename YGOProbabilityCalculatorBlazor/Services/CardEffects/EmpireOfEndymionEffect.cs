using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class EmpireOfEndymionEffect : ICardEffectDefinition {
    public int CanonicalCardId => 34041788;
    public string Name => "Empire of Endymion";
    public int Revision => 1;
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => "Search Regulus, the Prince of Endymion or a monster that mentions it";
    public string Limitations => "The optional Spellcaster Special Summon and destruction replacement effects are outside the opening-hand search model.";

    public DrawEffectPlan Compile() => new(null, true, 0, CardSelector.Any, 0, 0, CardSelector.Any,
        Search: new(CardSelector.AnyOfSelectors([
            CardSelector.CatalogIdentity(96228804),
            CardSelector.AllOfSelectors([
                CardSelector.AllOf(["metadata:kind:monster"]),
                CardSelector.Mentions("Regulus, the Prince of Endymion")
            ])
        ])));
}
