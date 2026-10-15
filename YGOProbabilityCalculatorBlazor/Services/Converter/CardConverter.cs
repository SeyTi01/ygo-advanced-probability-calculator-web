using System.Text.Json;
using System.Text.Json.Serialization;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.Converter;

public class CardConverter : JsonConverter<Card> {
    public override Card Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        JsonElement root = document.RootElement;

        List<CategoryBase> categories = JsonSerializer.Deserialize<List<CategoryBase>>(
            root.GetProperty("Categories").GetRawText(),
            options) ?? [];

        if (categories.Any(static category => category is null)) {
            throw new JsonException("Card contains missing categories.");
        }

        int copies = root.GetProperty("Copies").GetInt32();
        string? name = root.GetProperty("Name").GetString();
        // Older session files predate Active; keep their entries enabled.
        bool active = !root.TryGetProperty("Active", out JsonElement activeProperty) || activeProperty.GetBoolean();
        string? id = null;

        if (root.TryGetProperty("Id", out JsonElement idProperty)) {
            if (idProperty.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(id = idProperty.GetString())) {
                throw new JsonException("Card ID must be a non-empty string.");
            }
        }

        int? externalCardId = root.TryGetProperty("ExternalCardId", out JsonElement externalProperty) &&
            externalProperty.ValueKind != JsonValueKind.Null
                ? externalProperty.GetInt32()
                : null;

        List<string> manualKeys = root.TryGetProperty("ManualMetadataCategoryKeys", out JsonElement manualProperty)
            ? JsonSerializer.Deserialize<List<string>>(manualProperty.GetRawText(), options)
                ?? throw new JsonException("Manual card properties must be an array.")
            : [];

        JsonProperty[] drawProperties = root.EnumerateObject()
            .Where(property => property.Name.Equals("DrawCount", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (drawProperties.Length > 1) {
            throw new JsonException("Draw count is duplicated.");
        }

        int? drawCount = null;
        if (drawProperties.Length == 1 && drawProperties[0].Value.ValueKind != JsonValueKind.Null) {
            JsonElement value = drawProperties[0].Value;
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int count) || count is < 1 or > 3) {
                throw new JsonException("Draw count must be 1, 2, 3, or absent.");
            }

            drawCount = count;
        }

        JsonProperty[] limitProperties = root.EnumerateObject()
            .Where(property => property.Name.Equals("DrawOncePerTurn", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (limitProperties.Length > 1) {
            throw new JsonException("Draw once-per-turn setting is duplicated.");
        }

        bool drawOncePerTurn = true;
        if (limitProperties.Length == 1) {
            JsonElement value = limitProperties[0].Value;
            if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) {
                throw new JsonException("Draw once-per-turn setting must be a boolean.");
            }

            drawOncePerTurn = value.GetBoolean();
        }

        JsonProperty[] discardProperties = root.EnumerateObject()
            .Where(property => property.Name.Equals("PostDrawDiscardCount", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (discardProperties.Length > 1) {
            throw new JsonException("Post-draw discard count is duplicated.");
        }

        int discardCount = 0;
        if (discardProperties.Length == 1) {
            JsonElement value = discardProperties[0].Value;
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out discardCount)
                || discardCount is < 0 or > 2 || (discardCount > 0 && drawCount is null)) {
                throw new JsonException("Post-draw discard requires a draw effect and must be 0, 1, or 2.");
            }
        }

        CardSelector selector = CardSelector.Any;
        JsonProperty[] selectorProperties = root.EnumerateObject()
            .Where(property => property.Name.Equals("PostDrawSelector", StringComparison.OrdinalIgnoreCase)).ToArray();
        JsonProperty[] failureProperties = root.EnumerateObject()
            .Where(property => property.Name.Equals("PostDrawFailureBehavior", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (selectorProperties.Length > 1 || failureProperties.Length > 1) {
            throw new JsonException("Post-draw requirement fields are duplicated.");
        }
        if (selectorProperties.Length == 1) {
            selector = JsonSerializer.Deserialize<CardSelector>(selectorProperties[0].Value.GetRawText(), options)
                ?? throw new JsonException("Missing post-draw selector.");
        }
        if (failureProperties.Length == 1 && (failureProperties[0].Value.ValueKind != JsonValueKind.String
            || failureProperties[0].Value.GetString() != "FailBranch")) {
            throw new JsonException("Unsupported post-draw failure behavior.");
        }

        JsonProperty[] costProperties = root.EnumerateObject()
            .Where(property => property.Name.Equals("PreDrawRemovalCount", StringComparison.OrdinalIgnoreCase)).ToArray();
        JsonProperty[] costSelectors = root.EnumerateObject()
            .Where(property => property.Name.Equals("PreDrawSelector", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (costProperties.Length > 1 || costSelectors.Length > 1) {
            throw new JsonException("Pre-draw cost fields are duplicated.");
        }
        int costCount = 0;
        if (costProperties.Length == 1 && (costProperties[0].Value.ValueKind != JsonValueKind.Number
            || !costProperties[0].Value.TryGetInt32(out costCount))) {
            throw new JsonException("Pre-draw count must be an integer.");
        }
        CardSelector costSelector = costSelectors.Length == 0 ? CardSelector.Any
            : JsonSerializer.Deserialize<CardSelector>(costSelectors[0].Value.GetRawText(), options)
                ?? throw new JsonException("Missing pre-draw selector.");

        JsonProperty[] deckCostProperties = root.EnumerateObject()
            .Where(property => property.Name.Equals("PreDrawDeckRemovalCount", StringComparison.OrdinalIgnoreCase)).ToArray();
        int deckCost = 0;
        if (deckCostProperties.Length > 1 || (deckCostProperties.Length == 1
            && (deckCostProperties[0].Value.ValueKind != JsonValueKind.Number || !deckCostProperties[0].Value.TryGetInt32(out deckCost)))) {
            throw new JsonException("Hidden deck removal must be a single integer.");
        }

        JsonProperty[] millProperties = root.EnumerateObject()
            .Where(property => property.Name.Equals("PostDrawDeckMillCount", StringComparison.OrdinalIgnoreCase)).ToArray();
        int millCount = 0;
        if (millProperties.Length > 1 || (millProperties.Length == 1
            && (millProperties[0].Value.ValueKind != JsonValueKind.Number || !millProperties[0].Value.TryGetInt32(out millCount)))) {
            throw new JsonException("Post-draw milling must be a single integer.");
        }

        JsonProperty[] profileProperties = root.EnumerateObject()
            .Where(property => property.Name.Equals("EffectProfile", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (profileProperties.Length > 1) {
            throw new JsonException("Effect profile is duplicated.");
        }
        EffectProfile? profile = profileProperties.Length == 0 ? null
            : JsonSerializer.Deserialize<EffectProfile>(profileProperties[0].Value.GetRawText(), options)
                ?? throw new JsonException("Missing effect profile.");

        bool ReadRestriction(string name) {
            JsonProperty[] properties = root.EnumerateObject()
                .Where(property => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (properties.Length > 1 || (properties.Length == 1
                && properties[0].Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))) {
                throw new JsonException($"{name} must be a single boolean.");
            }

            return properties.Length == 1 && properties[0].Value.GetBoolean();
        }

        JsonProperty[] searches = root.EnumerateObject()
            .Where(property => property.Name.Equals("Search", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (searches.Length > 1) {
            throw new JsonException("Search operation is duplicated.");
        }
        SearchEffectPlan? search = searches.Length == 0 ? null
            : JsonSerializer.Deserialize<SearchEffectPlan>(searches[0].Value.GetRawText(), options)
                ?? throw new JsonException("Missing search operation.");

        bool firstActivationOnly = ReadRestriction("FirstActivationOnly");
        bool blocksLaterDraws = ReadRestriction("BlocksLaterDraws");
        try {
            return new Card(
                categories,
                copies,
                name,
                active,
                id,
                externalCardId,
                manualKeys,
                drawCount,
                drawOncePerTurn,
                discardCount,
                selector,
                preDrawRemovalCount: costCount,
                preDrawSelector: costSelector,
                preDrawDeckRemovalCount: deckCost,
                effectProfile: profile,
                postDrawDeckMillCount: millCount,
                firstActivationOnly: firstActivationOnly,
                blocksLaterDraws: blocksLaterDraws,
                search: search
            );
        }
        catch (ArgumentException exception) {
            throw new JsonException("Invalid card configuration.", exception);
        }
    }

    public override void Write(Utf8JsonWriter writer, Card value, JsonSerializerOptions options) {
        writer.WriteStartObject();
        writer.WritePropertyName("Categories");
        JsonSerializer.Serialize(writer, value.Categories.Select(category => new CategoryBase(category.Name, category.Source, category.MetadataKey)).ToArray(), options);
        writer.WriteNumber("Copies", value.Copies);
        writer.WriteString("Name", value.Name);
        writer.WriteBoolean("Active", value.Active);
        writer.WriteString("Id", value.Id);

        if (value.ExternalCardId is { } externalCardId) {
            writer.WriteNumber("ExternalCardId", externalCardId);
        }

        writer.WritePropertyName("ManualMetadataCategoryKeys");
        JsonSerializer.Serialize(writer, value.ManualMetadataCategoryKeys.Order(StringComparer.Ordinal), options);
        if (value.DrawCount is { } drawCount) {
            writer.WriteNumber("DrawCount", drawCount);
        }

        // Omission keeps earlier schema-4 sessions and ordinary cards compatible.
        if (!value.DrawOncePerTurn) {
            writer.WriteBoolean("DrawOncePerTurn", false);
        }

        if (value.PostDrawDiscardCount > 0) {
            writer.WriteNumber("PostDrawDiscardCount", value.PostDrawDiscardCount);
        }

        if (value.PostDrawSelector.Kind != CardSelectorKind.Any) {
            writer.WritePropertyName("PostDrawSelector");
            JsonSerializer.Serialize(writer, value.PostDrawSelector, options);
            writer.WriteString("PostDrawFailureBehavior", value.PostDrawFailureBehavior.ToString());
        }

        if (value.PreDrawRemovalCount > 0) {
            writer.WriteNumber("PreDrawRemovalCount", value.PreDrawRemovalCount);
        }
        if (value.PreDrawSelector.Kind != CardSelectorKind.Any) {
            writer.WritePropertyName("PreDrawSelector");
            JsonSerializer.Serialize(writer, value.PreDrawSelector, options);
        }

        if (value.PreDrawDeckRemovalCount > 0) {
            writer.WriteNumber("PreDrawDeckRemovalCount", value.PreDrawDeckRemovalCount);
        }

        if (value.PostDrawDeckMillCount > 0) {
            writer.WriteNumber("PostDrawDeckMillCount", value.PostDrawDeckMillCount);
        }

        if (value.FirstActivationOnly) {
            writer.WriteBoolean("FirstActivationOnly", true);
        }
        if (value.BlocksLaterDraws) {
            writer.WriteBoolean("BlocksLaterDraws", true);
        }

        if (value.EffectProfile != EffectProfile.Legacy) {
            writer.WritePropertyName("EffectProfile");
            JsonSerializer.Serialize(writer, value.EffectProfile, options);
        }

        if (value.Search is not null) {
            writer.WritePropertyName("Search");
            JsonSerializer.Serialize(writer, value.Search, options);
        }

        writer.WriteEndObject();
    }
}
