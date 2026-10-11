using System.Text.Json;
using System.Text.Json.Serialization;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.Converter;

public class ComboCategoryConverter : JsonConverter<ComboCategory> {
    public override ComboCategory Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) {
            throw new JsonException("A category requirement must be an object.");
        }

        bool composite = root.TryGetProperty("Selector", out JsonElement selectorJson);
        if (composite) {
            string[] allowed = ["Selector", "CategoryBases", "MinCount", "MaxCount", "MaximumMode"];
            JsonProperty[] properties = root.EnumerateObject().ToArray();
            if (properties.Any(property => !allowed.Contains(property.Name))
                || properties.Select(property => property.Name).Distinct().Count() != properties.Length) {
                throw new JsonException("Unknown or duplicate selector requirement fields.");
            }
            CardSelector selector = JsonSerializer.Deserialize<CardSelector>(selectorJson.GetRawText(), options)!;
            if (!root.TryGetProperty("CategoryBases", out JsonElement categoriesJson)
                || categoriesJson.ValueKind != JsonValueKind.Array || categoriesJson.GetArrayLength() is 0 or > 2048) {
                throw new JsonException("Selector category definitions are required.");
            }
            try {
                CategoryBase[] categories = JsonSerializer.Deserialize<CategoryBase[]>(categoriesJson.GetRawText(), options)!;
                ComboCategory requirement = ComboCategory.SameCard(
                    categories,
                    root.GetProperty("MinCount").GetInt32(),
                    root.GetProperty("MaxCount").GetInt32(),
                    RequirementMaximumModeJson.Read(root)
                );
                if (!requirement.Selector.Equals(selector)) {
                    throw new JsonException("Selector must match its category definitions.");
                }
                return requirement;
            }
            catch (ArgumentException exception) {
                throw new JsonException("Invalid selector requirement.", exception);
            }
        }
        if (root.TryGetProperty("CategoryBases", out _)) {
            throw new JsonException("Selector is required for composite category definitions.");
        }
        CategoryBase baseCategory = JsonSerializer.Deserialize<CategoryBase>(
            root.GetProperty("BaseCategory").GetRawText(),
            options
        ) ?? throw new JsonException("BaseCategory is required");

        int minCount = root.GetProperty("MinCount").GetInt32();
        int maxCount = root.GetProperty("MaxCount").GetInt32();

        try {
            return new ComboCategory(baseCategory, minCount, maxCount, RequirementMaximumModeJson.Read(root));
        }
        catch (ArgumentException exception) {
            throw new JsonException("Invalid category requirement bounds.", exception);
        }
    }

    public override void Write(Utf8JsonWriter writer, ComboCategory value, JsonSerializerOptions options) {
        writer.WriteStartObject();
        if (value.Selector.Kind == CardSelectorKind.Category) {
            writer.WritePropertyName("BaseCategory");
            JsonSerializer.Serialize(writer, value.BaseCategory, options);
        }
        else {
            writer.WritePropertyName("Selector");
            JsonSerializer.Serialize(writer, value.Selector, options);
            writer.WritePropertyName("CategoryBases");
            JsonSerializer.Serialize(writer, value.CategoryBases, options);
        }
        writer.WriteNumber("MinCount", value.MinCount);
        writer.WriteNumber("MaxCount", value.MaxCount);
        writer.WriteString("MaximumMode", value.MaximumMode.ToString());
        writer.WriteEndObject();
    }
}
