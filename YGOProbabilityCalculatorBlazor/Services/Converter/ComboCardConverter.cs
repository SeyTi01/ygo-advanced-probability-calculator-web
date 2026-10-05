using System.Text.Json;
using System.Text.Json.Serialization;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.Converter;

public class ComboCardConverter : JsonConverter<ComboCard>
{
    public override ComboCard Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        JsonElement root = document.RootElement;

        try
        {
            return new ComboCard(
                root.GetProperty("CardId").GetString() ?? throw new JsonException("CardId is required."),
                root.GetProperty("MinCount").GetInt32(),
                root.GetProperty("MaxCount").GetInt32(),
                RequirementMaximumModeJson.Read(root));
        }
        catch (ArgumentException ex)
        {
            throw new JsonException("Invalid card requirement bounds.", ex);
        }
    }

    public override void Write(Utf8JsonWriter writer, ComboCard value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("CardId", value.CardId);
        writer.WriteNumber("MinCount", value.MinCount);
        writer.WriteNumber("MaxCount", value.MaxCount);
        writer.WriteString("MaximumMode", value.MaximumMode.ToString());
        writer.WriteEndObject();
    }
}
