using System.Text.Json;
using System.Text.Json.Serialization;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.Converter;

public class CategoryBaseConverter : JsonConverter<CategoryBase> {
    public override CategoryBase Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        var name = root.GetProperty("Name").GetString();
        return new CategoryBase(name ?? throw new JsonException("Name is required"));
    }

    public override void Write(Utf8JsonWriter writer, CategoryBase value, JsonSerializerOptions options) {
        writer.WriteStartObject();
        writer.WriteString("Name", value.Name);
        writer.WriteEndObject();
    }
}
