using System.Globalization;
using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Services.DeckImport;

// Owns persisted provider metadata and partial-update semantics. The caller keeps
// lookup/publication gates; this cache never requests HTTP or starts catalog work.
internal sealed class CardMetadataCache {
    private const string CacheKey = "cardCache";
    private const int CacheSchemaVersion = 2;
    private static readonly TimeSpan CacheTimeToLive = TimeSpan.FromDays(7);
    private readonly ILocalStorageService _localStorage;
    private readonly TimeProvider _timeProvider;
    private readonly Lazy<Task> _loadTask;
    private Snapshot _state = new(CacheSchemaVersion, null, new());

    internal CardMetadataCache(ILocalStorageService localStorage, TimeProvider timeProvider) {
        _localStorage = localStorage;
        _timeProvider = timeProvider;
        _loadTask = new(async () => _state = await ReadAsync());
    }

    internal Task EnsureLoadedAsync() => _loadTask.Value;

    internal IEnumerable<CardInfo> Cards => _state.Cards.Values;

    internal bool TryGet(int id, out CardInfo card) => _state.Cards.TryGetValue(id, out card);

    internal bool IsFresh {
        get {
            if (_state.SchemaVersion != CacheSchemaVersion || _state.LastFullRefreshUtc is not DateTimeOffset lastRefresh) {
                return false;
            }

            TimeSpan age = _timeProvider.GetUtcNow() - lastRefresh;

            return age >= TimeSpan.Zero && age < CacheTimeToLive;
        }
    }

    internal void Store(int queryId, CardInfo card) {
        _state.Cards[queryId] = card;

        // A targeted enrichment is never evidence of a full metadata refresh.
        if (_state.SchemaVersion != CacheSchemaVersion) {
            _state = new(CacheSchemaVersion, null, _state.Cards);
        }
    }

    // Artwork publishes under the caller's gate, then saves this detached envelope
    // outside it. Other lookup routes save their live envelope while holding it.
    internal Snapshot CaptureSnapshot() => new(_state.SchemaVersion, _state.LastFullRefreshUtc, new(_state.Cards));

    internal async Task SaveAsync(Snapshot? snapshot = null) {
        try {
            await _localStorage.SetItemAsync(CacheKey, snapshot ?? _state);
        }
        catch {
            // The in-memory cache remains usable for the current page session.
        }
    }

    private async Task<Snapshot> ReadAsync() {
        try {
            string? json = await _localStorage.GetRawItemAsync(CacheKey);

            if (string.IsNullOrWhiteSpace(json)) {
                return new Snapshot(CacheSchemaVersion, null, new());
            }

            using JsonDocument document = JsonDocument.Parse(json);

            if (document.RootElement.ValueKind != JsonValueKind.Object) {
                return new Snapshot(CacheSchemaVersion, null, new());
            }

            JsonElement root = document.RootElement;

            if (CardMetadataJson.TryGetProperty(root, "schemaVersion", out _)) {
                return TryReadVersionedCache(root, out Snapshot versionedCache)
                    ? versionedCache
                    : new Snapshot(CacheSchemaVersion, null, new());
            }

            return TryReadLegacyCache(root, out Snapshot legacyCache)
                ? legacyCache
                : new Snapshot(CacheSchemaVersion, null, new());
        }
        catch {
            // Malformed data and browser-storage failures must not break imports.
            return new Snapshot(CacheSchemaVersion, null, new());
        }
    }

    private static bool TryReadVersionedCache(JsonElement root, out Snapshot cache) {
        cache = new Snapshot(CacheSchemaVersion, null, new());
        JsonElement cardsProperty = default;
        int version = 0;

        bool hasInvalidCacheHeader = !CardMetadataJson.TryGetProperty(root, "schemaVersion", out JsonElement versionProperty)
            || versionProperty.ValueKind != JsonValueKind.Number
            || !versionProperty.TryGetInt32(out version)
            || version is not (1 or CacheSchemaVersion)
            || !CardMetadataJson.TryGetProperty(root, "cards", out cardsProperty)
            || cardsProperty.ValueKind != JsonValueKind.Object;

        if (hasInvalidCacheHeader) {
            return false;
        }

        DateTimeOffset? lastFullRefreshUtc = null;
        DateTimeOffset parsedTimestamp = default;

        bool hasValidTimestamp = CardMetadataJson.TryGetProperty(root, "lastFullRefreshUtc", out JsonElement timestampProperty)
            && timestampProperty.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(
                timestampProperty.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out parsedTimestamp
            );

        if (hasValidTimestamp) {
            lastFullRefreshUtc = parsedTimestamp;
        }

        Dictionary<int, CardInfo> cards = new();

        foreach (JsonProperty property in cardsProperty.EnumerateObject()) {
            if (!int.TryParse(property.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)
                || property.Value.ValueKind != JsonValueKind.Object
                || !CardMetadataJson.TryGetProperty(property.Value, "name", out JsonElement nameProperty)
                || nameProperty.ValueKind != JsonValueKind.String) {

                return false;
            }

            string? name = nameProperty.GetString();

            if (string.IsNullOrWhiteSpace(name)) {
                return false;
            }

            cards[id] = version == 1 ? new CardInfo { Id = id, Name = name } : CardMetadataJson.ReadCached(property.Value, id)!;
        }

        if (cards.Count == 0) {
            return false;
        }

        cache = new(version, lastFullRefreshUtc, cards);

        return true;
    }

    private static bool TryReadLegacyCache(JsonElement root, out Snapshot cache) {
        Dictionary<int, CardInfo> cards = new();

        foreach (JsonProperty property in root.EnumerateObject()) {
            if (!int.TryParse(property.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) ||
                property.Value.ValueKind != JsonValueKind.String) {
                cache = new Snapshot(CacheSchemaVersion, null, new());

                return false;
            }

            string? name = property.Value.GetString();

            if (!string.IsNullOrWhiteSpace(name)) {
                cards[id] = new CardInfo { Id = id, Name = name };
            }
        }

        cache = new(1, null, cards);

        return true;
    }

    internal sealed record Snapshot(int SchemaVersion, DateTimeOffset? LastFullRefreshUtc, Dictionary<int, CardInfo> Cards);
}
