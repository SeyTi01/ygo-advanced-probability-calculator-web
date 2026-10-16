using System.Text.Json;
using System.Text.Json.Nodes;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.Session;

/// <summary>
/// Applies saved-session schema migrations before the current model is deserialized.
/// </summary>
public sealed class SessionSchemaMigrator {
    private static readonly IReadOnlyDictionary<int, Action<JsonObject>> Migrations =
        new Dictionary<int, Action<JsonObject>> {
            [0] = MigrateV0ToV1,
            [1] = MigrateV1ToV2,
            [2] = root => SetSchemaVersion(root, 3),
            [3] = MigrateV3ToV4,
            [4] = MigrateV4ToV5,
            [5] = MigrateV5ToV6,
            // Grouped selectors and typed numeric predicates require a new reader.
            // Existing saved operations and their provenance remain authoritative.
            [6] = root => SetSchemaVersion(root, 7),
            [7] = MigrateV7ToV8,
            [8] = root => SetSchemaVersion(root, 9)
        };

    public string MigrateToCurrent(string json) {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement rootElement = document.RootElement;

        if (rootElement.ValueKind != JsonValueKind.Object) {
            throw new JsonException("Session root must be a JSON object.");
        }

        JsonProperty[] schemaVersionProperties = rootElement.EnumerateObject()
            .Where(static property => string.Equals(
                property.Name,
                nameof(SessionState.SchemaVersion),
                StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (schemaVersionProperties.Length > 1) {
            throw new JsonException("Session schema version field is duplicated.");
        }

        int sourceVersion = schemaVersionProperties.Length == 0
            ? 0
            : ReadSchemaVersion(schemaVersionProperties[0].Value);

        if (sourceVersion < 0) {
            throw new JsonException("Session schema version must be a non-negative integer.");
        }

        if (sourceVersion > SessionState.CurrentSchemaVersion) {
            throw UnsupportedVersion(sourceVersion);
        }

        JsonProperty[] modes = rootElement.EnumerateObject().Where(property =>
            property.Name.Equals(nameof(SessionState.CalculationModel), StringComparison.OrdinalIgnoreCase)).ToArray();
        JsonProperty[] legacy = rootElement.EnumerateObject().Where(property =>
            property.Name.Equals("DrawEffectsEnabled", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (modes.Length > 1 || legacy.Length > 1 || (sourceVersion < 8 && modes.Length > 0)
            || (sourceVersion >= 8 && legacy.Length > 0)) {
            throw new JsonException("Session has duplicated, obsolete or contradictory calculation mode fields.");
        }
        if (modes.Length == 1 && (modes[0].Value.ValueKind != JsonValueKind.String
            || !Enum.GetNames<CalculationModel>().Contains(modes[0].Value.GetString(), StringComparer.Ordinal))) {
            throw new JsonException("Unknown session calculation mode.");
        }
        if (legacy.Length == 1 && legacy[0].Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) {
            throw new JsonException("Legacy draw-effects setting must be a boolean.");
        }

        if (sourceVersion == SessionState.CurrentSchemaVersion) {
            return json;
        }

        JsonObject root = JsonNode.Parse(json) as JsonObject ?? throw new JsonException("Session root must be a JSON object.");
        if (sourceVersion <= 3) {
            RemoveUnreleasedRules(root);
        }
        int version = sourceVersion;

        while (version < SessionState.CurrentSchemaVersion) {
            if (!Migrations.TryGetValue(version, out Action<JsonObject> migration)) {
                throw UnsupportedVersion(version);
            }

            migration(root);
            version++;
        }

        JsonSerializerOptions options = new() { WriteIndented = true };
        return root.ToJsonString(options);
    }

    private static void RemoveUnreleasedRules(JsonNode? node) {
        if (node is JsonObject value) {
            foreach (string key in value.Select(property => property.Key).ToArray()) {
                if (key.Equals("Rule", StringComparison.OrdinalIgnoreCase)) {
                    value.Remove(key);
                }
                else {
                    RemoveUnreleasedRules(value[key]);
                }
            }
        }
        else if (node is JsonArray array) {
            foreach (JsonNode? child in array) {
                RemoveUnreleasedRules(child);
            }
        }
    }

    private static int ReadSchemaVersion(JsonElement value) {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int version)) {
            throw new JsonException("Session schema version must be a non-negative integer.");
        }

        return version;
    }

    private static void MigrateV0ToV1(JsonObject root) => SetSchemaVersion(root, 1);

    private static void MigrateV7ToV8(JsonObject root) {
        string? field = root.Select(property => property.Key).SingleOrDefault(name =>
            name.Equals("DrawEffectsEnabled", StringComparison.OrdinalIgnoreCase));
        bool enabled = field is null || root[field]!.GetValue<bool>();
        if (field is not null) {
            root.Remove(field);
        }
        root[nameof(SessionState.CalculationModel)] = enabled ? "Fast" : "OpeningHand";
        SetSchemaVersion(root, 8);
    }

    private static void MigrateV5ToV6(JsonObject root) {
        // Old readers ignored Search extension fields. Preserve their modeled meaning.
        JsonArray? cards = root.FirstOrDefault(property => property.Key.Equals("Cards", StringComparison.OrdinalIgnoreCase)).Value as JsonArray;
        foreach (JsonObject card in cards?.OfType<JsonObject>() ?? []) {
            foreach (string field in card.Select(property => property.Key)
                .Where(name => name.Equals("Search", StringComparison.OrdinalIgnoreCase) || name.Equals("OncePerTurn", StringComparison.OrdinalIgnoreCase)).ToArray()) {
                card.Remove(field);
            }
        }
        SetSchemaVersion(root, 6);
    }

    private static void MigrateV4ToV5(JsonObject root) {
        // Schema 4 readers ignored these extension fields. Preserve that meaning;
        // only schema 5 can opt a saved card into an activation cost.
        JsonArray? cards = root.FirstOrDefault(property => property.Key.Equals("Cards", StringComparison.OrdinalIgnoreCase)).Value as JsonArray;
        foreach (JsonObject card in cards?.OfType<JsonObject>() ?? []) {
            string[] fields = card.Select(property => property.Key)
                .Where(name => name.Equals("PreDrawRemovalCount", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("PreDrawSelector", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("PreDrawDeckRemovalCount", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("PostDrawDeckMillCount", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("FirstActivationOnly", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("BlocksLaterDraws", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("EffectProfile", StringComparison.OrdinalIgnoreCase)).ToArray();
            foreach (string field in fields) {
                card.Remove(field);
            }
        }
        SetSchemaVersion(root, 5);
    }

    private static void MigrateV3ToV4(JsonObject root) {
        // Earlier schemas did not model effects. Preserve their ordinary-card meaning,
        // even if an unrecognized extension field was present in an old file.
        JsonArray? cards = root.FirstOrDefault(property => property.Key.Equals("Cards", StringComparison.OrdinalIgnoreCase)).Value as JsonArray;
        foreach (JsonObject card in cards?.OfType<JsonObject>() ?? []) {
            string[] fields = card.Select(property => property.Key)
                .Where(name => name.Equals("DrawCount", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("DrawOncePerTurn", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("PostDrawSelector", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("PostDrawFailureBehavior", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("PostDrawDiscardCount", StringComparison.OrdinalIgnoreCase)).ToArray();
            foreach (string field in fields) {
                card.Remove(field);
            }
        }

        SetSchemaVersion(root, 4);
    }

    private static void MigrateV1ToV2(JsonObject root) {
        // Pre-v2 categories are always user definitions, at every persisted location.
        foreach (JsonNode? category in Array(root, "Categories")) {
            Classify(category);
        }

        foreach (JsonNode? card in Array(root, "Cards")) {
            foreach (JsonNode? category in Array(card, "Categories")) {
                Classify(category);
            }
        }

        foreach (JsonNode? combo in Array(root, "Combos")) {
            foreach (JsonNode? constraint in Array(combo, "Categories")) {
                Classify(Property(constraint, "BaseCategory"));
                FixMaximum(constraint);
            }

            foreach (JsonNode? constraint in Array(combo, "Cards")) {
                FixMaximum(constraint);
            }
        }

        SetSchemaVersion(root, 2);

        static void FixMaximum(JsonNode? node) {
            if (node is not JsonObject requirement) {
                return;
            }

            string[] keys = requirement.Select(static property => property.Key)
                .Where(static key => key.Equals("MaximumMode", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            foreach (string key in keys) {
                requirement.Remove(key);
            }

            requirement["MaximumMode"] = "Fixed";
        }

        static void Classify(JsonNode? node) {
            if (node is not JsonObject category) {
                return;
            }

            string[] keys = category.Select(static property => property.Key)
                .Where(static key => key.Equals("Source", StringComparison.OrdinalIgnoreCase) || key.Equals("MetadataKey", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            foreach (string key in keys) {
                category.Remove(key);
            }

            category["Source"] = "User";
        }

        static JsonNode? Property(JsonNode? node, string name) {
            if (node is not JsonObject objectNode) {
                return null;
            }

            return objectNode.FirstOrDefault(property => property.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
        }

        static IEnumerable<JsonNode?> Array(JsonNode? node, string name) {
            return Property(node, name) as JsonArray ?? [];
        }
    }

    private static void SetSchemaVersion(JsonObject root, int version) {
        string? propertyName = root.Select(static property => property.Key)
            .SingleOrDefault(name => string.Equals(
                name,
                nameof(SessionState.SchemaVersion),
                StringComparison.OrdinalIgnoreCase));

        root[propertyName ?? nameof(SessionState.SchemaVersion)] = version;
    }

    private static InvalidOperationException UnsupportedVersion(int version) =>
        new($"Unsupported session schema version {version}. This application supports session schema version {SessionState.CurrentSchemaVersion}.");
}
