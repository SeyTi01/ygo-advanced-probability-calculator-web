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
            [0] = MigrateV0ToV1
        };

    public string MigrateToCurrent(string json) {
        using var document = JsonDocument.Parse(json);
        var rootElement = document.RootElement;
        if (rootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException("Session root must be a JSON object.");

        var schemaVersionProperties = rootElement.EnumerateObject()
            .Where(property => string.Equals(property.Name, nameof(SessionState.SchemaVersion), StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (schemaVersionProperties.Length > 1)
            throw new JsonException("Session schema version field is duplicated.");

        var sourceVersion = schemaVersionProperties.Length == 0
            ? 0
            : ReadSchemaVersion(schemaVersionProperties[0].Value);

        if (sourceVersion < 0)
            throw new JsonException("Session schema version must be a non-negative integer.");

        if (sourceVersion > SessionState.CurrentSchemaVersion)
            throw UnsupportedVersion(sourceVersion);

        if (sourceVersion == SessionState.CurrentSchemaVersion)
            return json;

        var root = JsonNode.Parse(json) as JsonObject
            ?? throw new JsonException("Session root must be a JSON object.");

        var version = sourceVersion;
        while (version < SessionState.CurrentSchemaVersion) {
            if (!Migrations.TryGetValue(version, out var migration))
                throw UnsupportedVersion(version);

            migration(root);
            version++;
        }

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static int ReadSchemaVersion(JsonElement value) {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var version))
            throw new JsonException("Session schema version must be a non-negative integer.");

        return version;
    }

    private static void MigrateV0ToV1(JsonObject root) => SetSchemaVersion(root, 1);

    private static void SetSchemaVersion(JsonObject root, int version) {
        var propertyName = root.Select(property => property.Key)
            .SingleOrDefault(name => string.Equals(name, nameof(SessionState.SchemaVersion), StringComparison.OrdinalIgnoreCase));

        root[propertyName ?? nameof(SessionState.SchemaVersion)] = version;
    }

    private static InvalidOperationException UnsupportedVersion(int version) =>
        new($"Unsupported session schema version {version}. This application supports session schema version {SessionState.CurrentSchemaVersion}.");
}
