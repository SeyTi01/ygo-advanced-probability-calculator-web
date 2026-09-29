using System.Text.Json;
using System.Text.Json.Serialization;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.Converter;

public class CardConverter : JsonConverter<Card> {
    public override Card Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        var categories = JsonSerializer.Deserialize<List<CategoryBase>>(
            root.GetProperty("Categories").GetRawText(),
            options) ?? [];

        var copies = root.GetProperty("Copies").GetInt32();
        var name = root.GetProperty("Name").GetString();
        // Older session files predate Active; keep their entries enabled.
        var active = root.TryGetProperty("Active", out var activeProperty)
            ? activeProperty.GetBoolean()
            : true;

        string? id = null;
        if (root.TryGetProperty("Id", out var idProperty)) {
            if (idProperty.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(id = idProperty.GetString()))
                throw new JsonException("Card ID must be a non-empty string.");
        }
        return new Card(categories, copies, name, active, id);
    }

    public override void Write(Utf8JsonWriter writer, Card value, JsonSerializerOptions options) {
        writer.WriteStartObject();
        writer.WritePropertyName("Categories");
        JsonSerializer.Serialize(writer, value.Categories, options);
        writer.WriteNumber("Copies", value.Copies);
        writer.WriteString("Name", value.Name);
        writer.WriteBoolean("Active", value.Active);
        writer.WriteString("Id", value.Id);
        writer.WriteEndObject();
    }
}
