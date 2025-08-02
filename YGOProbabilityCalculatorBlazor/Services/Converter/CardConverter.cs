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

        return new Card(categories, copies, name);
    }

    public override void Write(Utf8JsonWriter writer, Card value, JsonSerializerOptions options) {
        writer.WriteStartObject();
        writer.WritePropertyName("Categories");
        JsonSerializer.Serialize(writer, value.Categories, options);
        writer.WriteNumber("Copies", value.Copies);
        writer.WriteString("Name", value.Name);
        writer.WriteEndObject();
    }
}
