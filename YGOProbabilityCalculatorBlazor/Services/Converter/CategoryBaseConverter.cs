using System.Text.Json;
using System.Text.Json.Serialization;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.Converter;

public class CategoryBaseConverter : JsonConverter<CategoryBase> {
    public override CategoryBase Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        JsonElement root = document.RootElement;
        string? name = root.GetProperty("Name").GetString();
        CategorySource source = CategorySource.User;

        if (root.TryGetProperty("Source", out JsonElement sourceProperty)
            && (sourceProperty.ValueKind != JsonValueKind.String
            || !Enum.TryParse(sourceProperty.GetString(), out source)
            || !Enum.IsDefined(source))
        ) {
            throw new JsonException("Unknown category source.");
        }

        string? key = root.TryGetProperty("MetadataKey", out JsonElement keyProperty)
            ? keyProperty.GetString()
            : null;

        JsonProperty[] rules = root.EnumerateObject().Where(property => property.Name.Equals("Rule", StringComparison.OrdinalIgnoreCase)).ToArray();

        if (rules.Length > 1 || (rules.Length == 1 && rules[0].Name != "Rule")) {
            throw new JsonException("Invalid or duplicate category rule field.");
        }

        CardSelector? rule = rules.Length == 0
            ? null
            : JsonSerializer.Deserialize<CardSelector>(rules[0].Value.GetRawText(), options) ?? throw new JsonException("Category rule must be a nonempty metadata selector.");

        try {
            return new CategoryBase(name ?? throw new JsonException("Name is required"), source, key, rule);
        }
        catch (ArgumentException exception) {
            throw new JsonException("Invalid category identity.", exception);
        }
    }

    public override void Write(Utf8JsonWriter writer, CategoryBase value, JsonSerializerOptions options) {
        writer.WriteStartObject();
        writer.WriteString("Name", value.Name);
        writer.WriteString("Source", value.Source.ToString());

        if (value.MetadataKey is not null) {
            writer.WriteString("MetadataKey", value.MetadataKey);
        }

        if (value.Rule is not null) {
            writer.WritePropertyName("Rule");
            JsonSerializer.Serialize(writer, value.Rule, options);
        }

        writer.WriteEndObject();
    }
}
