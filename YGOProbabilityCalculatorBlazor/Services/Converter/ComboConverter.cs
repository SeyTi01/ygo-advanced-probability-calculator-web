using System.Text.Json;
using System.Text.Json.Serialization;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.Converter;

public class ComboConverter : JsonConverter<Combo> {
    public override Combo Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        var categories = JsonSerializer.Deserialize<List<ComboCategory>>(
            root.GetProperty("Categories").GetRawText(),
            options) ?? [];

        return new Combo(categories);
    }

    public override void Write(Utf8JsonWriter writer, Combo value, JsonSerializerOptions options) {
        writer.WriteStartObject();
        writer.WritePropertyName("Categories");
        JsonSerializer.Serialize(writer, value.Categories, options);
        writer.WriteEndObject();
    }
}
