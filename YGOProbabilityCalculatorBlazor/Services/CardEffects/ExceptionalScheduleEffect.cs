using YGOProbabilityCalculatorBlazor.Localization;
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
    public string Summary => UiText.English("Effects.52782439.Summary");
    public string Limitations => UiText.English("Effects.52782439.Limitations");

    public DrawEffectPlan Compile() => new(null, true, 0, CardSelector.Any, 0, 0, CardSelector.Any,
        Search: new(Targets));
}
