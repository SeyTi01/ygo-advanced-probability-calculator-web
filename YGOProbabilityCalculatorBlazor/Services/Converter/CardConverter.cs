using System.Text.Json;
using System.Text.Json.Serialization;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.Converter;

public class CardConverter : JsonConverter<Card> {
    public override Card Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        JsonElement root = document.RootElement;

        List<CategoryBase> categories = JsonSerializer.Deserialize<List<CategoryBase>>(
            root.GetProperty("Categories").GetRawText(),
            options) ?? [];

        if (categories.Any(static category => category is null)) {
            throw new JsonException("Card contains missing categories.");
        }

        int copies = root.GetProperty("Copies").GetInt32();
        string? name = root.GetProperty("Name").GetString();
        // Older session files predate Active; keep their entries enabled.
        bool active = !root.TryGetProperty("Active", out JsonElement activeProperty) || activeProperty.GetBoolean();
        string? id = null;

        if (root.TryGetProperty("Id", out JsonElement idProperty)) {
            if (idProperty.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(id = idProperty.GetString())) {
                throw new JsonException("Card ID must be a non-empty string.");
            }
        }

        int? externalCardId = root.TryGetProperty("ExternalCardId", out JsonElement externalProperty) &&
            externalProperty.ValueKind != JsonValueKind.Null
                ? externalProperty.GetInt32()
                : null;

        List<string> manualKeys = root.TryGetProperty("ManualMetadataCategoryKeys", out JsonElement manualProperty)
            ? JsonSerializer.Deserialize<List<string>>(manualProperty.GetRawText(), options)
                ?? throw new JsonException("Manual card properties must be an array.")
            : [];

        JsonProperty[] drawProperties = root.EnumerateObject()
            .Where(property => property.Name.Equals("DrawCount", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (drawProperties.Length > 1) {
            throw new JsonException("Draw count is duplicated.");
        }

        int? drawCount = null;
        if (drawProperties.Length == 1 && drawProperties[0].Value.ValueKind != JsonValueKind.Null) {
            JsonElement value = drawProperties[0].Value;
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int count) || count is < 1 or > 3) {
                throw new JsonException("Draw count must be 1, 2, 3, or absent.");
            }

            drawCount = count;
        }

        JsonProperty[] limitProperties = root.EnumerateObject()
            .Where(property => property.Name.Equals("DrawOncePerTurn", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (limitProperties.Length > 1) {
            throw new JsonException("Draw once-per-turn setting is duplicated.");
        }

        bool drawOncePerTurn = true;
        if (limitProperties.Length == 1) {
            JsonElement value = limitProperties[0].Value;
            if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) {
                throw new JsonException("Draw once-per-turn setting must be a boolean.");
            }

            drawOncePerTurn = value.GetBoolean();
        }

        try {
            return new Card(categories, copies, name, active, id, externalCardId, manualKeys, drawCount, drawOncePerTurn);
        }
        catch (ArgumentException exception) {
            throw new JsonException("Invalid manual card properties.", exception);
        }
    }

    public override void Write(Utf8JsonWriter writer, Card value, JsonSerializerOptions options) {
        writer.WriteStartObject();
        writer.WritePropertyName("Categories");
        JsonSerializer.Serialize(writer, value.Categories, options);
        writer.WriteNumber("Copies", value.Copies);
        writer.WriteString("Name", value.Name);
        writer.WriteBoolean("Active", value.Active);
        writer.WriteString("Id", value.Id);

        if (value.ExternalCardId is { } externalCardId) {
            writer.WriteNumber("ExternalCardId", externalCardId);
        }

        writer.WritePropertyName("ManualMetadataCategoryKeys");
        JsonSerializer.Serialize(writer, value.ManualMetadataCategoryKeys.Order(StringComparer.Ordinal), options);
        if (value.DrawCount is { } drawCount) {
            writer.WriteNumber("DrawCount", drawCount);
        }

        // Omission keeps earlier schema-4 sessions and ordinary cards compatible.
        if (!value.DrawOncePerTurn) {
            writer.WriteBoolean("DrawOncePerTurn", false);
        }

        writer.WriteEndObject();
    }
}
