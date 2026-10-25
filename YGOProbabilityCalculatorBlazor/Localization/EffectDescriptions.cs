using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;

namespace YGOProbabilityCalculatorBlazor.Localization;

public static class EffectDescriptions {
    public static EffectiveCardEffect Describe(Card card, CardEffectRegistry? registry = null) {
        EffectiveCardEffect effect = (registry ?? CardEffectRegistry.Default).Describe(card);
        if (!effect.UsesCurrentDescription) { return effect; }

        int identity = effect.Maintained!.CanonicalCardId;
        return effect with {
            Summary = UiText.Get($"Effects.{identity}.Summary"),
            Limitations = UiText.Get($"Effects.{identity}.Limitations")
        };
    }
}
