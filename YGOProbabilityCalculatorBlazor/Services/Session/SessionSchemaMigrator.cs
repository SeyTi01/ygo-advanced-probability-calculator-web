using System.Text.Json;
using System.Text.Json.Nodes;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.Session;

/// <summary>
/// Applies saved-session schema migrations before the current model is deserialized.
/// </summary>
public sealed class SessionSchemaMigrator
{
    private static readonly IReadOnlyDictionary<int, Action<JsonObject>> Migrations =
        new Dictionary<int, Action<JsonObject>>
        {
            [0] = MigrateV0ToV1,
            [1] = MigrateV1ToV2,
            [2] = root => SetSchemaVersion(root, 3),
        };

    public string MigrateToCurrent(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement rootElement = document.RootElement;

        if (rootElement.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Session root must be a JSON object.");
        }

        JsonProperty[] schemaVersionProperties =
        [
            .. rootElement
                .EnumerateObject()
                .Where(property =>
                    string.Equals(
                        property.Name,
                        nameof(SessionState.SchemaVersion),
                        StringComparison.OrdinalIgnoreCase
                    )
                ),
        ];

        if (schemaVersionProperties.Length > 1)
        {
            throw new JsonException("Session schema version field is duplicated.");
        }

        int sourceVersion =
            schemaVersionProperties.Length == 0
                ? 0
                : ReadSchemaVersion(schemaVersionProperties[0].Value);

        if (sourceVersion < 0)
        {
            throw new JsonException("Session schema version must be a non-negative integer.");
        }

        if (sourceVersion > SessionState.CurrentSchemaVersion)
        {
            throw UnsupportedVersion(sourceVersion);
        }

        if (sourceVersion == SessionState.CurrentSchemaVersion)
        {
            return json;
        }

        JsonObject root =
            JsonNode.Parse(json) as JsonObject
            ?? throw new JsonException("Session root must be a JSON object.");

        int version = sourceVersion;

        while (version < SessionState.CurrentSchemaVersion)
        {
            if (!Migrations.TryGetValue(version, out Action<JsonObject>? migration))
            {
                throw UnsupportedVersion(version);
            }

            migration(root);
            version++;
        }

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static int ReadSchemaVersion(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int version))
        {
            throw new JsonException("Session schema version must be a non-negative integer.");
        }

        return version;
    }

    private static void MigrateV0ToV1(JsonObject root) => SetSchemaVersion(root, 1);

    private static void MigrateV1ToV2(JsonObject root)
    {
        // Pre-v2 categories are always user definitions, at every persisted location.
        foreach (JsonNode? category in Array(root, "Categories"))
        {
            Classify(category);
        }

        foreach (JsonNode? card in Array(root, "Cards"))
        {
            foreach (JsonNode? category in Array(card, "Categories"))
            {
                Classify(category);
            }
        }

        foreach (JsonNode? combo in Array(root, "Combos"))
        {
            foreach (JsonNode? constraint in Array(combo, "Categories"))
            {
                Classify(Property(constraint, "BaseCategory"));
                FixMaximum(constraint);
            }

            foreach (JsonNode? constraint in Array(combo, "Cards"))
            {
                FixMaximum(constraint);
            }
        }

        SetSchemaVersion(root, 2);

        static void FixMaximum(JsonNode? node)
        {
            if (node is not JsonObject requirement)
            {
                return;
            }

            foreach (
                string key in requirement
                    .Select(p => p.Key)
                    .Where(key => key.Equals("MaximumMode", StringComparison.OrdinalIgnoreCase))
                    .ToArray()
            )
            {
                requirement.Remove(key);
            }

            requirement["MaximumMode"] = "Fixed";
        }

        static void Classify(JsonNode? node)
        {
            if (node is not JsonObject category)
            {
                return;
            }

            foreach (
                string key in category
                    .Select(p => p.Key)
                    .Where(key =>
                        key.Equals("Source", StringComparison.OrdinalIgnoreCase)
                        || key.Equals("MetadataKey", StringComparison.OrdinalIgnoreCase)
                    )
                    .ToArray()
            )
            {
                category.Remove(key);
            }

            category["Source"] = "User";
        }

        static JsonNode? Property(JsonNode? node, string name) =>
            node is JsonObject obj
                ? obj.FirstOrDefault(p =>
                    p.Key.Equals(name, StringComparison.OrdinalIgnoreCase)
                ).Value
                : null;

        static IEnumerable<JsonNode?> Array(JsonNode? node, string name) =>
            Property(node, name) is JsonArray array ? array : [];
    }

    private static void SetSchemaVersion(JsonObject root, int version)
    {
        string? propertyName = root.Select(property => property.Key)
            .SingleOrDefault(name =>
                string.Equals(
                    name,
                    nameof(SessionState.SchemaVersion),
                    StringComparison.OrdinalIgnoreCase
                )
            );

        root[propertyName ?? nameof(SessionState.SchemaVersion)] = version;
    }

    private static InvalidOperationException UnsupportedVersion(int version) =>
        new(
            $"Unsupported session schema version {version}. This application supports session schema version {SessionState.CurrentSchemaVersion}."
        );
}
