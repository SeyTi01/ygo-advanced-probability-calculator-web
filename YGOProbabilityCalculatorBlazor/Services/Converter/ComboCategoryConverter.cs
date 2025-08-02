using System.Text.Json;
using System.Text.Json.Serialization;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.Converter;

public class ComboCategoryConverter : JsonConverter<ComboCategory> {
    public override ComboCategory Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        var baseCategory = JsonSerializer.Deserialize<CategoryBase>(
            root.GetProperty("BaseCategory").GetRawText(),
            options) ?? throw new JsonException("BaseCategory is required");

        var minCount = root.GetProperty("MinCount").GetInt32();
        var maxCount = root.GetProperty("MaxCount").GetInt32();

        return new ComboCategory(baseCategory, minCount, maxCount);
    }

    public override void Write(Utf8JsonWriter writer, ComboCategory value, JsonSerializerOptions options) {
        writer.WriteStartObject();
        writer.WritePropertyName("BaseCategory");
        JsonSerializer.Serialize(writer, value.BaseCategory, options);
        writer.WriteNumber("MinCount", value.MinCount);
        writer.WriteNumber("MaxCount", value.MaxCount);
        writer.WriteEndObject();
    }
}
