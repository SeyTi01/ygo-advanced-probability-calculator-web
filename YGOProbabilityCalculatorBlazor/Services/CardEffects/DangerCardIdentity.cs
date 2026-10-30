using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.CardEffects;

// Only maintained, catalog-verified passcodes and aliases establish a Danger!
// identity. Saved provenance, names and editable category metadata cannot do so.
internal static class DangerCardIdentity {
    internal static int? Resolve(Card card, CardEffectRegistry? registry = null) {
        MaintainedCardEffect? maintained = card.ExternalCardId is int identity
            ? (registry ?? CardEffectRegistry.Default).Find(identity) : null;

        return maintained?.Plan.Operation == DrawOperation.DangerReveal ? maintained.CanonicalCardId : null;
    }

    internal static int RequireSource(Card card) => Resolve(card)
        ?? throw new ArgumentException("Danger! reveal requires a verified canonical card ID or artwork alias.");
}
