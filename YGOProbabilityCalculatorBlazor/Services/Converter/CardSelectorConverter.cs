using System.Text.Json;
using System.Text.Json.Serialization;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.Converter;

public sealed class CardSelectorConverter : JsonConverter<CardSelector> {
    public override bool HandleNull => true;

    public override CardSelector Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) {
            throw new JsonException("A card selector must be an object.");
        }
        JsonProperty[] properties = root.EnumerateObject().ToArray();
        if (properties.Length != 2 || properties.Count(property => property.Name == "Kind") != 1
            || properties.Count(property => property.Name == "Categories") != 1) {
            throw new JsonException("A card selector requires exactly Kind and Categories.");
        }
        JsonElement kind = root.GetProperty("Kind");
        JsonElement categories = root.GetProperty("Categories");
        if (kind.ValueKind != JsonValueKind.String || categories.ValueKind != JsonValueKind.Array
            || categories.EnumerateArray().Any(category => category.ValueKind != JsonValueKind.String)) {
            throw new JsonException("Invalid card selector fields.");
        }
        CardSelectorKind operation = kind.GetString() switch {
            "Any" => CardSelectorKind.Any,
            "Category" => CardSelectorKind.Category,
            "AnyOf" => CardSelectorKind.AnyOf,
            _ => throw new JsonException("Unsupported card selector operator.")
        };
        try {
            return new(operation, categories.EnumerateArray().Select(category => category.GetString()!));
        }
        catch (ArgumentException exception) {
            throw new JsonException("Invalid card selector.", exception);
        }
    }

    public override void Write(Utf8JsonWriter writer, CardSelector value, JsonSerializerOptions options) {
        writer.WriteStartObject();
        writer.WriteString("Kind", value.Kind.ToString());
        writer.WriteStartArray("Categories");
        foreach (string identity in value.CategoryIdentities) {
            writer.WriteStringValue(identity);
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}
