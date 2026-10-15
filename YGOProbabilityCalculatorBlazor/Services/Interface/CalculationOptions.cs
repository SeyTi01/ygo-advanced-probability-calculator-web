using System.Text.Json.Serialization;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.Interface;

// Every call executes the saved plans. Reject obsolete version-local worker options
// rather than silently changing the requested mathematical model.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CalculationOptions(
    DrawActivationPriority? DrawActivationPriority = null,
    // Native API compatibility only. Worker JSON rejects the old checkbox field;
    // new requests always derive certification from Mode and execution proof.
    [property: JsonIgnore] bool CertifySearchAvailability = false,
    CalculationModel Mode = CalculationModel.Fast
);
