using System.Text.Json;
using System.Text.Json.Serialization;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.Converter;

public class CardConverter : JsonConverter<Card>
{
    public override Card Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using JsonDocument doc = JsonDocument.ParseValue(ref reader);
        JsonElement root = doc.RootElement;

        List<CategoryBase> categories = JsonSerializer.Deserialize<List<CategoryBase>>(
            root.GetProperty("Categories").GetRawText(),
            options) ?? [];

        if (categories.Any(category => category is null))
        {
            throw new JsonException("Card contains missing categories.");
        }

        int copies = root.GetProperty("Copies").GetInt32();
        string? name = root.GetProperty("Name").GetString();
        // Older session files predate Active; keep their entries enabled.
        bool active = ! root.TryGetProperty("Active", out JsonElement activeProperty) || activeProperty.GetBoolean();

        string? id = null;

        if (root.TryGetProperty("Id", out JsonElement idProperty))
        {
            if (idProperty.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(id = idProperty.GetString()))
            {
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

        try
        {
            return new Card(categories, copies, name, active, id, externalCardId, manualKeys);
        }
        catch (ArgumentException ex)
        {
            throw new JsonException("Invalid manual card properties.", ex);
        }
    }

    public override void Write(Utf8JsonWriter writer, Card value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("Categories");
        JsonSerializer.Serialize(writer, value.Categories, options);
        writer.WriteNumber("Copies", value.Copies);
        writer.WriteString("Name", value.Name);
        writer.WriteBoolean("Active", value.Active);
        writer.WriteString("Id", value.Id);

        if (value.ExternalCardId is { } externalCardId)
        {
            writer.WriteNumber("ExternalCardId", externalCardId);
        }

        writer.WritePropertyName("ManualMetadataCategoryKeys");
        JsonSerializer.Serialize(writer, value.ManualMetadataCategoryKeys.Order(StringComparer.Ordinal), options);
        writer.WriteEndObject();
    }
}
