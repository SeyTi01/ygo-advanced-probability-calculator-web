using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

public sealed class ExceptionalScheduleEffect : ICardEffectDefinition {
    private static readonly CardSelector Targets = CardSelector.AnyOfSelectors([
        CardSelector.CatalogIdentity(97520701),
        CardSelector.CatalogIdentity(25274141)
    ]);

    public int CanonicalCardId => 52782439;
    public string Name => "Exceptional Schedule";
    public int Revision => 1;
    public EffectSupport Support => EffectSupport.ModeledWithinRules;
    public string Summary => "Search 1 Special Schedule or Urgent Schedule; once per turn.";
    public string Limitations => "The optional Diagram Token summon is outside this Deck-search model.";

    public DrawEffectPlan Compile() => new(null, true, 0, CardSelector.Any, 0, 0, CardSelector.Any,
        Search: new(Targets));
}
