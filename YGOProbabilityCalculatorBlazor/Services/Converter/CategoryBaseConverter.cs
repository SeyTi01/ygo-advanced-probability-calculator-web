using System.Text.Json;
using System.Text.Json.Serialization;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.Converter;

public class CategoryBaseConverter : JsonConverter<CategoryBase>
{
    public override CategoryBase Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        using JsonDocument doc = JsonDocument.ParseValue(ref reader);
        JsonElement root = doc.RootElement;
        string? name = root.GetProperty("Name").GetString();
        CategorySource source = CategorySource.User;

        if (
            root.TryGetProperty("Source", out JsonElement sourceProperty)
            && (
                sourceProperty.ValueKind != JsonValueKind.String
                || !Enum.TryParse(sourceProperty.GetString(), out source)
                || !Enum.IsDefined(source)
            )
        )
        {
            throw new JsonException("Unknown category source.");
        }

        string? key = root.TryGetProperty("MetadataKey", out JsonElement keyProperty)
            ? keyProperty.GetString()
            : null;

        try
        {
            return new CategoryBase(
                name ?? throw new JsonException("Name is required"),
                source,
                key
            );
        }
        catch (ArgumentException ex)
        {
            throw new JsonException("Invalid category identity.", ex);
        }
    }

    public override void Write(
        Utf8JsonWriter writer,
        CategoryBase value,
        JsonSerializerOptions options
    )
    {
        writer.WriteStartObject();
        writer.WriteString("Name", value.Name);
        writer.WriteString("Source", value.Source.ToString());

        if (value.MetadataKey is not null)
        {
            writer.WriteString("MetadataKey", value.MetadataKey);
        }

        writer.WriteEndObject();
    }
}
