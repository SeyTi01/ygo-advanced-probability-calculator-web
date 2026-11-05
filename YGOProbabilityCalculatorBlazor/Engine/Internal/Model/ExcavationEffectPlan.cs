using System.Text.Json.Serialization;

namespace CardProbability.Internal.Model;

// One randomly revealed pool supplies exactly one card. A branch activates at
// most one excavation, selected before the pool is known, ahead of all searches.
internal sealed record ExcavationEffectPlan(int Count, bool RequiresNoPriorEffectDraw = false) {
    public void Validate() {
        if (Count < 1) {
            throw new ArgumentOutOfRangeException(nameof(Count), "Excavation count must be positive.");
        }
    }
}
