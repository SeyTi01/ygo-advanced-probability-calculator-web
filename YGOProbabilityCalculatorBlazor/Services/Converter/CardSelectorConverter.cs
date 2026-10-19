using System.Text.Json;
using System.Text.Json.Serialization;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.Converter;

public sealed class CardSelectorConverter : JsonConverter<CardSelector> {
    public override bool HandleNull => true;

    public override CardSelector Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        int nodes = 0;
        int references = 0;
        try {
            return ReadSelector(document.RootElement, 1, ref nodes, ref references);
        }
        catch (ArgumentException exception) {
            throw new JsonException("Invalid card selector.", exception);
        }
    }

    private static CardSelector ReadSelector(JsonElement root, int depth, ref int nodes, ref int references) {
        if (root.ValueKind != JsonValueKind.Object || depth > CardSelector.MaximumDepth || ++nodes > CardSelector.MaximumNodes) {
            throw new JsonException("Selector exceeds its depth or node limit.");
        }
        JsonProperty[] properties = root.EnumerateObject().ToArray();
        if (root.TryGetProperty("Kind", out JsonElement numericKind) && numericKind.ValueKind == JsonValueKind.String && numericKind.GetString() == "NumericRange") {
            string[] allowed = ["Kind", "Property", "Minimum", "Maximum", "ObjectiveOnly"];
            if (properties.Length != allowed.Length || properties.Any(property => !allowed.Contains(property.Name))
                || properties.Select(property => property.Name).Distinct().Count() != allowed.Length
                || root.GetProperty("Property").ValueKind != JsonValueKind.String
                || !Enum.TryParse(root.GetProperty("Property").GetString(), out NumericPropertyKind propertyKind)
                || !Enum.IsDefined(propertyKind) || root.GetProperty("Property").GetString() != propertyKind.ToString()
                || root.GetProperty("ObjectiveOnly").ValueKind is not (JsonValueKind.True or JsonValueKind.False)) {
                throw new JsonException("Invalid numeric range fields.");
            }
            int? Bound(string name) {
                JsonElement bound = root.GetProperty(name);
                if (bound.ValueKind == JsonValueKind.Null) { return null; }
                if (bound.ValueKind != JsonValueKind.Number || !bound.TryGetInt32(out int value)) { throw new JsonException("Numeric bounds require whole numbers."); }
                return value;
            }
            return CardSelector.ForNumericRange(new(propertyKind, Bound("Minimum"), Bound("Maximum"), root.GetProperty("ObjectiveOnly").GetBoolean()));
        }

        if (properties.Length != 2 || properties.Count(property => property.Name == "Kind") != 1
            || properties.Count(property => property.Name is "Categories" or "Children" or "Minimum") != 1) {
            throw new JsonException("A selector requires Kind and exactly one operand field.");
        }
        JsonElement kind = root.GetProperty("Kind");
        if (kind.ValueKind != JsonValueKind.String) {
            throw new JsonException("A selector kind must be a string.");
        }
        string operation = kind.GetString()!;
        if (root.TryGetProperty("Minimum", out JsonElement minimum)) {
            if (operation != "LevelAtLeast" || minimum.ValueKind != JsonValueKind.Number || !minimum.TryGetInt32(out int threshold)) {
                throw new JsonException("A Level predicate requires an integer minimum.");
            }
            return CardSelector.LevelAtLeast(threshold);
        }
        if (root.TryGetProperty("Children", out JsonElement children)) {
            if (operation is not ("AnyOf" or "AllOf" or "Objective" or "Not") || children.ValueKind != JsonValueKind.Array
                || children.GetArrayLength() is 0 or > CardSelector.MaximumNodes) {
                throw new JsonException("Invalid composite selector.");
            }
            List<CardSelector> operands = [];
            foreach (JsonElement child in children.EnumerateArray()) {
                operands.Add(ReadSelector(child, depth + 1, ref nodes, ref references));
            }
            return operation switch {
                "AnyOf" => CardSelector.AnyOfSelectors(operands),
                "AllOf" => CardSelector.AllOfSelectors(operands),
                "Objective" when operands.Count == 1 => CardSelector.Objective(operands[0]),
                "Not" when operands.Count == 1 => CardSelector.Not(operands[0]),
                _ => throw new JsonException("Unary selector requires exactly one child.")
            };
        }
        JsonElement categories = root.GetProperty("Categories");
        if (categories.ValueKind != JsonValueKind.Array
            || (references += categories.GetArrayLength()) > CardSelector.MaximumCategoryReferences
            || categories.EnumerateArray().Any(category => category.ValueKind != JsonValueKind.String)) {
            throw new JsonException("Invalid category references.");
        }
        CardSelectorKind categoryOperation = operation switch {
            "Any" => CardSelectorKind.Any,
            "Category" => CardSelectorKind.Category,
            "AnyOf" => CardSelectorKind.AnyOf,
            "AllOf" => CardSelectorKind.AllOf,
            _ => throw new JsonException("Unsupported card selector operator.")
        };
        return new(categoryOperation, categories.EnumerateArray().Select(category => category.GetString()!));
    }

    public override void Write(Utf8JsonWriter writer, CardSelector value, JsonSerializerOptions options) {
        writer.WriteStartObject();
        writer.WriteString("Kind", value.Kind.ToString());
        if (value.Kind == CardSelectorKind.NumericRange) {
            NumericPropertyRange range = value.NumericRange!;
            writer.WriteString("Property", range.Property.ToString());
            if (range.Minimum is int minimum) { writer.WriteNumber("Minimum", minimum); } else { writer.WriteNull("Minimum"); }
            if (range.Maximum is int maximum) { writer.WriteNumber("Maximum", maximum); } else { writer.WriteNull("Maximum"); }
            writer.WriteBoolean("ObjectiveOnly", range.ObjectiveOnly);
        }
        else if (value.Kind == CardSelectorKind.LevelAtLeast) {
            writer.WriteNumber("Minimum", value.MinimumLevel!.Value);
        }
        else if (value.Children.Count > 0) {
            writer.WriteStartArray("Children");
            foreach (CardSelector child in value.AuthoredTerms) {
                JsonSerializer.Serialize(writer, child, options);
            }
            writer.WriteEndArray();
        }
        else {
            writer.WriteStartArray("Categories");
            foreach (string identity in value.AuthoredTerms.SelectMany(term => term.CategoryIdentities)) {
                writer.WriteStringValue(identity);
            }
            writer.WriteEndArray();
        }
        writer.WriteEndObject();
    }
}
