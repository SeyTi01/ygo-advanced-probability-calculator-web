using System.Text.Json;
using System.Text.Json.Serialization;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.Converter;

public class CardConverter : JsonConverter<Card> {
    private sealed class StrictJsonFieldReader(JsonElement root) {
        public JsonElement? FindUnique(string name, string duplicateError) {
            JsonElement? result = null;

            foreach (JsonProperty property in root.EnumerateObject()) {
                if (!property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }

                if (result is not null) {
                    throw new JsonException(duplicateError);
                }

                result = property.Value;
            }

            return result;
        }

        public static bool ReadBoolean(JsonElement value, string error) {
            if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) {
                throw new JsonException(error);
            }

            return value.GetBoolean();
        }

        public static int ReadInt32(JsonElement value, string error) {
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int result)) {
                throw new JsonException(error);
            }

            return result;
        }
    }

    public override Card Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        JsonElement root = document.RootElement;
        StrictJsonFieldReader fields = new(root);

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
        JsonElement? simulationFlag = fields.FindUnique("SimulationEnabled", "Simulation enablement must be a single boolean.");
        bool simulationEnabled = simulationFlag is null || StrictJsonFieldReader.ReadBoolean(
            simulationFlag.Value,
            "Simulation enablement must be a single boolean.");
        string? id = null;

        if (root.TryGetProperty("Id", out JsonElement idProperty)) {
            if (idProperty.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(id = idProperty.GetString())) {
                throw new JsonException("Card ID must be a non-empty string.");
            }
        }

        int? externalCardId = root.TryGetProperty("ExternalCardId", out JsonElement externalProperty) && externalProperty.ValueKind != JsonValueKind.Null
            ? externalProperty.GetInt32()
            : null;

        List<string> manualKeys = root.TryGetProperty("ManualMetadataCategoryKeys", out JsonElement manualProperty)
            ? JsonSerializer.Deserialize<List<string>>(manualProperty.GetRawText(), options) ?? throw new JsonException("Manual card properties must be an array.")
            : [];

        JsonElement? drawProperty = fields.FindUnique("DrawCount", "Draw count is duplicated.");
        int? drawCount = null;

        if (drawProperty is { ValueKind: not JsonValueKind.Null } value) {
            int count = StrictJsonFieldReader.ReadInt32(value, "Draw count must be 1, 2, 3, or absent.");

            if (count is < 1 or > 3) {
                throw new JsonException("Draw count must be 1, 2, 3, or absent.");
            }

            drawCount = count;
        }

        JsonElement? limitProperty = fields.FindUnique("DrawOncePerTurn", "Draw once-per-turn setting is duplicated.");
        bool drawOncePerTurn = true;

        if (limitProperty is JsonElement drawLimit) {
            drawOncePerTurn = StrictJsonFieldReader.ReadBoolean(drawLimit, "Draw once-per-turn setting must be a boolean.");
        }

        JsonElement? sharedLimit = fields.FindUnique("OncePerTurn", "Once-per-turn setting is duplicated.");
        bool? oncePerTurn = null;

        if (sharedLimit is JsonElement oncePerTurnValue) {
            oncePerTurn = StrictJsonFieldReader.ReadBoolean(oncePerTurnValue, "Once-per-turn setting must be a boolean.");
        }

        JsonElement? discardProperty = fields.FindUnique("PostDrawDiscardCount", "Post-draw discard count is duplicated.");
        int discardCount = 0;

        if (discardProperty is JsonElement discardValue) {
            discardCount = StrictJsonFieldReader.ReadInt32(discardValue, "Post-draw discard requires a draw effect and must be 0, 1, or 2.");

            if (discardCount is < 0 or > 2) {
                throw new JsonException("Post-draw discard requires a draw effect and must be 0, 1, or 2.");
            }
        }

        CardSelector selector = CardSelector.Any;
        JsonElement? selectorProperty = fields.FindUnique("PostDrawSelector", "Post-draw requirement fields are duplicated.");
        JsonElement? failureProperty = fields.FindUnique("PostDrawFailureBehavior", "Post-draw requirement fields are duplicated.");

        if (selectorProperty is JsonElement postDrawSelector) {
            selector = JsonSerializer.Deserialize<CardSelector>(postDrawSelector.GetRawText(), options) ?? throw new JsonException("Missing post-draw selector.");
        }

        if (failureProperty is JsonElement failureBehavior
            && (failureBehavior.ValueKind != JsonValueKind.String || failureBehavior.GetString() != "FailBranch")) {
            throw new JsonException("Unsupported post-draw failure behavior.");
        }

        JsonElement? costProperty = fields.FindUnique("PreDrawRemovalCount", "Pre-draw cost fields are duplicated.");
        JsonElement? costSelectorProperty = fields.FindUnique("PreDrawSelector", "Pre-draw cost fields are duplicated.");
        int costCount = 0;

        if (costProperty is JsonElement preDrawCount) {
            costCount = StrictJsonFieldReader.ReadInt32(preDrawCount, "Pre-draw count must be an integer.");
        }

        CardSelector costSelector = costSelectorProperty is null
            ? CardSelector.Any
            : JsonSerializer.Deserialize<CardSelector>(costSelectorProperty.Value.GetRawText(), options) ?? throw new JsonException("Missing pre-draw selector.");

        JsonElement? deckCostProperty = fields.FindUnique("PreDrawDeckRemovalCount", "Hidden deck removal must be a single integer.");
        int deckCost = 0;

        if (deckCostProperty is JsonElement hiddenDeckCost) {
            deckCost = StrictJsonFieldReader.ReadInt32(hiddenDeckCost, "Hidden deck removal must be a single integer.");
        }

        JsonElement? millProperty = fields.FindUnique("PostDrawDeckMillCount", "Post-draw milling must be a single integer.");
        int millCount = 0;

        if (millProperty is JsonElement millValue) {
            millCount = StrictJsonFieldReader.ReadInt32(millValue, "Post-draw milling must be a single integer.");
        }

        JsonElement? profileProperty = fields.FindUnique("EffectProfile", "Effect profile is duplicated.");
        EffectProfile? profile = profileProperty is null
            ? null
            : JsonSerializer.Deserialize<EffectProfile>(profileProperty.Value.GetRawText(), options) ?? throw new JsonException("Missing effect profile.");

        bool ReadRestriction(string name) {
            JsonElement? property = fields.FindUnique(name, $"{name} must be a single boolean.");
            return property is JsonElement value && StrictJsonFieldReader.ReadBoolean(value, $"{name} must be a single boolean.");
        }

        JsonElement? searchProperty = fields.FindUnique("Search", "Search operation is duplicated.");
        SearchEffectPlan? search = searchProperty is null
            ? null
            : JsonSerializer.Deserialize<SearchEffectPlan>(searchProperty.Value.GetRawText(), options) ?? throw new JsonException("Missing search operation.");

        JsonElement? excavationProperty = fields.FindUnique("Excavation", "Excavation operation is duplicated.");
        ExcavationEffectPlan? excavation = excavationProperty is null
            ? null
            : JsonSerializer.Deserialize<ExcavationEffectPlan>(excavationProperty.Value.GetRawText(), options) ?? throw new JsonException("Missing excavation operation.");

        int? ReadInteger(string name) {
            JsonElement? property = fields.FindUnique(name, $"{name} must be a single integer.");
            return property is JsonElement value
                ? StrictJsonFieldReader.ReadInt32(value, $"{name} must be a single integer.")
                : null;
        }

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
                search: search,
                oncePerTurn: oncePerTurn,
                activationLimit: ReadInteger("ActivationLimit"),
                minimumHandSize: ReadInteger("MinimumHandSize") ?? 0,
                simulationEnabled: simulationEnabled,
                excavation: excavation
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
        writer.WriteBoolean("SimulationEnabled", value.SimulationEnabled);
        writer.WriteString("Id", value.Id);

        if (value.ExternalCardId is { } externalCardId) {
            writer.WriteNumber("ExternalCardId", externalCardId);
        }

        writer.WritePropertyName("ManualMetadataCategoryKeys");
        JsonSerializer.Serialize(writer, value.ManualMetadataCategoryKeys.Order(StringComparer.Ordinal), options);

        if (value.DrawCount is { } drawCount) {
            writer.WriteNumber("DrawCount", drawCount);
        }

        if (value.Search is not null || value.Excavation is not null) {
            // Explicit shared flag distinguishes new search limits from the old,
            // ignored DrawOncePerTurn field. Existing draw wire meanings stay intact.
            writer.WriteBoolean("OncePerTurn", value.OncePerTurn);
        }
        else if (!value.DrawOncePerTurn) {
            writer.WriteBoolean("DrawOncePerTurn", false);
        }

        if (value.EffectPlan.ActivationLimit is int limit) {
            writer.WriteNumber("ActivationLimit", limit);
        }

        if (value.MinimumHandSize > 0) {
            writer.WriteNumber("MinimumHandSize", value.MinimumHandSize);
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

        if (value.Excavation is not null) {
            writer.WritePropertyName("Excavation");
            JsonSerializer.Serialize(writer, value.Excavation, options);
        }

        writer.WriteEndObject();
    }
}
