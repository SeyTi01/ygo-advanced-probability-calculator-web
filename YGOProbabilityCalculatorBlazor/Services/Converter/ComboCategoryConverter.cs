using System.Text.Json;
using System.Text.Json.Serialization;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.Converter;

public class ComboCategoryConverter : JsonConverter<ComboCategory>
{
    public override ComboCategory Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using JsonDocument doc = JsonDocument.ParseValue(ref reader);
        JsonElement root = doc.RootElement;

        CategoryBase baseCategory = JsonSerializer.Deserialize<CategoryBase>(
            root.GetProperty("BaseCategory").GetRawText(),
            options) ?? throw new JsonException("BaseCategory is required");

        int minCount = root.GetProperty("MinCount").GetInt32();
        int maxCount = root.GetProperty("MaxCount").GetInt32();

        try
        {
            return new ComboCategory(baseCategory, minCount, maxCount, RequirementMaximumModeJson.Read(root));
        }
        catch (ArgumentException ex)
        {
            throw new JsonException("Invalid category requirement bounds.", ex);
        }
    }

    public override void Write(Utf8JsonWriter writer, ComboCategory value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("BaseCategory");
        JsonSerializer.Serialize(writer, value.BaseCategory, options);
        writer.WriteNumber("MinCount", value.MinCount);
        writer.WriteNumber("MaxCount", value.MaxCount);
        writer.WriteString("MaximumMode", value.MaximumMode.ToString());
        writer.WriteEndObject();
    }
}
