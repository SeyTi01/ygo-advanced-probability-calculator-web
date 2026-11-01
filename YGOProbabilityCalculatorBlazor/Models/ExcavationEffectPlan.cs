using System.Text.Json.Serialization;
using YGOProbabilityCalculatorBlazor.Services.Converter;

namespace YGOProbabilityCalculatorBlazor.Models;

// One randomly revealed pool supplies exactly one card. A branch activates at
// most one excavation, selected before the pool is known, ahead of all searches.
[JsonConverter(typeof(ExcavationEffectPlanConverter))]
public sealed record ExcavationEffectPlan(int Count, bool RequiresNoPriorEffectDraw = false) {
    public void Validate() {
        if (Count is < 1 or > 6) {
            throw new ArgumentOutOfRangeException(nameof(Count), "Excavation count must be between 1 and 6.");
        }
    }
}
