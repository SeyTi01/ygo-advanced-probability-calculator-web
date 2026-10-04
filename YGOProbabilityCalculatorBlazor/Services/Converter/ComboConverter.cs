using System.Text.Json;
using System.Text.Json.Serialization;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.Converter;

public class ComboConverter : JsonConverter<Combo> {
    public override Combo Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        CheckFields(root, "Categories", "Cards", "Name", "Active", "GroupId", "AlternativeGroups");
        var categories = JsonSerializer.Deserialize<List<ComboCategory>>(
            root.GetProperty("Categories").GetRawText(),
            options) ?? [];
        var cards = root.TryGetProperty("Cards", out var cardsProperty)
            ? JsonSerializer.Deserialize<List<ComboCard>>(cardsProperty.GetRawText(), options) ?? []
            : [];

        var name = root.TryGetProperty("Name", out var nameProperty)
            ? nameProperty.GetString()
            : null;
        // Older session files predate Active; keep their entries enabled.
        var active = root.TryGetProperty("Active", out var activeProperty)
            ? activeProperty.GetBoolean()
            : true;
        var groupId = root.TryGetProperty("GroupId", out var groupProperty)
            ? groupProperty.GetString()
            : null;

        var groups = new List<ComboAlternativeGroup>();
        var leafCount = 0;
        if (root.TryGetProperty("AlternativeGroups", out var groupArray)) {
            if (groupArray.ValueKind != JsonValueKind.Array || groupArray.GetArrayLength() > 2048)
                throw new JsonException("Invalid alternative groups.");
            foreach (var group in groupArray.EnumerateArray()) {
                CheckFields(group, "Alternatives");
                if (!group.TryGetProperty("Alternatives", out var alternatives) || alternatives.ValueKind != JsonValueKind.Array ||
                    alternatives.GetArrayLength() is 0 or > 2048) throw new JsonException("An OR group requires alternatives.");
                leafCount += alternatives.GetArrayLength();
                if (leafCount > 2048) throw new JsonException("Too many alternative requirements.");
                var leaves = new List<ComboAlternative>();
                foreach (var leaf in alternatives.EnumerateArray()) {
                    CheckFields(leaf, "Kind", "Category", "Card");
                    if (!leaf.TryGetProperty("Kind", out var kind)) throw new JsonException("Alternative kind is required.");
                    var hasCategory = leaf.TryGetProperty("Category", out var category);
                    var hasCard = leaf.TryGetProperty("Card", out var card);
                    if (hasCategory) CheckFields(category, "BaseCategory", "MinCount", "MaxCount", "MaximumMode");
                    if (hasCard) CheckFields(card, "CardId", "MinCount", "MaxCount", "MaximumMode");
                    if (kind.ValueKind != JsonValueKind.String) throw new JsonException("Invalid alternative kind.");
                    if (kind.GetString() == "Category" && hasCategory && !hasCard)
                        leaves.Add(ComboAlternative.For(JsonSerializer.Deserialize<ComboCategory>(category.GetRawText(), options)
                            ?? throw new JsonException("Missing category alternative.")));
                    else if (kind.GetString() == "Card" && hasCard && !hasCategory)
                        leaves.Add(ComboAlternative.For(JsonSerializer.Deserialize<ComboCard>(card.GetRawText(), options)
                            ?? throw new JsonException("Missing card alternative.")));
                    else throw new JsonException("Invalid alternative kind or target.");
                }
                groups.Add(new ComboAlternativeGroup(leaves));
            }
        }
        return new Combo(categories, name, active, groupId, cards, groups);
    }

    private static void CheckFields(JsonElement element, params string[] allowed) {
        if (element.ValueKind != JsonValueKind.Object) throw new JsonException("Expected expression object.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (!allowed.Contains(property.Name) || !seen.Add(property.Name))
                throw new JsonException("Unknown or duplicate expression field.");
    }

    public override void Write(Utf8JsonWriter writer, Combo value, JsonSerializerOptions options) {
        writer.WriteStartObject();

        writer.WritePropertyName("Categories");
        JsonSerializer.Serialize(writer, value.Categories, options);
        if (value.Cards.Count > 0) {
            writer.WritePropertyName("Cards");
            JsonSerializer.Serialize(writer, value.Cards, options);
        }
        if (value.AlternativeGroups.Count > 0) {
            writer.WriteStartArray("AlternativeGroups");
            foreach (var group in value.AlternativeGroups) {
                writer.WriteStartObject();
                writer.WriteStartArray("Alternatives");
                foreach (var alternative in group.Alternatives) {
                    writer.WriteStartObject();
                    writer.WriteString("Kind", alternative.Kind);
                    writer.WritePropertyName(alternative.Kind);
                    if (alternative.Category is { } category) JsonSerializer.Serialize(writer, category, options);
                    else JsonSerializer.Serialize(writer, alternative.Card, options);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }

        if (value.Name is not null) {
            writer.WritePropertyName("Name");
            writer.WriteStringValue(value.Name);
        }

        writer.WriteBoolean("Active", value.Active);
        if (value.GroupId is not null)
            writer.WriteString("GroupId", value.GroupId);
        writer.WriteEndObject();
    }
}
