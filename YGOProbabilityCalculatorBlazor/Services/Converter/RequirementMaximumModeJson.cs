using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.Converter;

internal static class RequirementMaximumModeJson {
    public static RequirementMaximumMode Read(JsonElement root) {
        JsonProperty[] properties = root.EnumerateObject()
            .Where(static property => property.Name.Equals("MaximumMode", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (properties.Length == 0) {
            return RequirementMaximumMode.Fixed;
        }

        if (properties.Length > 1) {
            throw new JsonException("Requirement maximum mode field is duplicated.");
        }

        JsonElement value = properties[0].Value;

        if (value.ValueKind == JsonValueKind.String) {
            return value.GetString() switch {
                "Fixed" => RequirementMaximumMode.Fixed,
                "HandSize" => RequirementMaximumMode.HandSize,
                _ => throw new JsonException("Unknown requirement maximum mode.")
            };
        }

        throw new JsonException("Requirement maximum mode must be Fixed or HandSize.");
    }
}
