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
        return ReadSelector(root, 0);
    }

    private static CardSelector ReadSelector(JsonElement root, int depth) {
        if (root.ValueKind != JsonValueKind.Object || depth > 1) {
            throw new JsonException("Selectors support only OR of atomic categories or AND groups.");
        }
        JsonProperty[] properties = root.EnumerateObject().ToArray();
        if (properties.Length != 2 || properties.Count(property => property.Name == "Kind") != 1
            || properties.Count(property => property.Name is "Categories" or "Children") != 1) {
            throw new JsonException("A card selector requires Kind and either Categories or Children.");
        }
        JsonElement kind = root.GetProperty("Kind");
        if (root.TryGetProperty("Children", out JsonElement children)) {
            if (depth != 0 || kind.ValueKind != JsonValueKind.String || kind.GetString() != "AnyOf"
                || children.ValueKind != JsonValueKind.Array || children.GetArrayLength() is 0 or > 2048) {
                throw new JsonException("Invalid composite selector.");
            }
            List<CardSelector> alternatives = [];
            int leaves = 0;
            foreach (JsonElement child in children.EnumerateArray()) {
                if (child.ValueKind != JsonValueKind.Object
                    || !child.TryGetProperty("Categories", out JsonElement atoms)
                    || atoms.ValueKind != JsonValueKind.Array || (leaves += atoms.GetArrayLength()) > 2048) {
                    throw new JsonException("An OR selector requires at most 2048 atomic category references.");
                }
                alternatives.Add(ReadSelector(child, depth + 1));
            }
            return CardSelector.AnyOfSelectors(alternatives);
        }
        JsonElement categories = root.GetProperty("Categories");
        if (kind.ValueKind != JsonValueKind.String || categories.ValueKind != JsonValueKind.Array
            || categories.GetArrayLength() > 2048
            || categories.EnumerateArray().Any(category => category.ValueKind != JsonValueKind.String)) {
            throw new JsonException("Invalid card selector fields.");
        }
        CardSelectorKind operation = kind.GetString() switch {
            "Any" => CardSelectorKind.Any,
            "Category" => CardSelectorKind.Category,
            "AnyOf" => CardSelectorKind.AnyOf,
            "AllOf" => CardSelectorKind.AllOf,
            _ => throw new JsonException("Unsupported card selector operator.")
        };
        if (depth > 0 && operation is not (CardSelectorKind.Category or CardSelectorKind.AllOf)) {
            throw new JsonException("An OR child must be an atomic category or an AND group.");
        }
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
        if (value.Children.Count > 0) {
            writer.WriteStartArray("Children");
            foreach (CardSelector child in value.Children) {
                JsonSerializer.Serialize(writer, child, options);
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
            return;
        }
        writer.WriteStartArray("Categories");
        foreach (string identity in value.CategoryIdentities) {
            writer.WriteStringValue(identity);
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}
