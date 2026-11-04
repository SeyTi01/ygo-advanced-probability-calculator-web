using System.Text.Json;
using System.Text.Json.Serialization;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.Converter;

public sealed class ExcavationEffectPlanConverter : JsonConverter<ExcavationEffectPlan> {
    public override ExcavationEffectPlan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) {
            throw new JsonException("Excavation must be an object.");
        }

        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        foreach (JsonProperty field in root.EnumerateObject()) {
            if (!names.Add(field.Name) || field.Name is not ("Count" or "RequiresNoPriorEffectDraw")) {
                throw new JsonException("Unknown or duplicated excavation field.");
            }
        }
        if (!root.TryGetProperty("Count", out JsonElement count) || count.ValueKind != JsonValueKind.Number
            || !count.TryGetInt32(out int amount) || amount is < 1 or > 6) {
            throw new JsonException("Excavation count must be between 1 and 6.");
        }

        bool requiresNoPriorEffectDraw = false;
        if (root.TryGetProperty("RequiresNoPriorEffectDraw", out JsonElement restriction)) {
            if (restriction.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) {
                throw new JsonException("Excavation draw restriction must be a boolean.");
            }
            requiresNoPriorEffectDraw = restriction.GetBoolean();
        }

        return new(amount, requiresNoPriorEffectDraw);
    }

    public override void Write(Utf8JsonWriter writer, ExcavationEffectPlan value, JsonSerializerOptions options) {
        writer.WriteStartObject();
        writer.WriteNumber("Count", value.Count);
        writer.WriteBoolean("RequiresNoPriorEffectDraw", value.RequiresNoPriorEffectDraw);
        writer.WriteEndObject();
    }
}
