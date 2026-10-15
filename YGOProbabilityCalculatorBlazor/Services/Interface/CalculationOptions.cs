using System.Text.Json.Serialization;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.Interface;

// Every call executes the saved plans. Reject obsolete version-local worker options
// rather than silently changing the requested mathematical model.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CalculationOptions(
    DrawActivationPriority? DrawActivationPriority = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool CertifySearchAvailability = false
);
