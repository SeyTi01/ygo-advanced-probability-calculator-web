using System.Globalization;
using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Services.DeckImport;

public class CardInfoService : ICardInfoService {
    private const string BulkApiUrl = "https://db.ygoprodeck.com/api/v7/cardinfo.php";
    private const string SingleApiTemplate = "https://db.ygoprodeck.com/api/v7/cardinfo.php?id={0}";
    private const string CacheKey = "cardCache";
    private const int CacheSchemaVersion = 2;
    private static readonly TimeSpan CacheTimeToLive = TimeSpan.FromDays(7);

    private readonly HttpClient _httpClient;
    private readonly ILocalStorageService _localStorage;
    private readonly TimeProvider _timeProvider;
    private readonly Lazy<Task> _initializeTask;
    private CardMetadataCache _cache = new();
    private readonly SemaphoreSlim _singleLookupGate = new(1, 1);
    private readonly HashSet<int> _singleLookups = [];

    public async Task<CardInfo> GetCardInfoAsync(int id) {
        await _initializeTask.Value;
        await _singleLookupGate.WaitAsync();
        try {
            if (!_cache.Cards.TryGetValue(id, out var card) ||
                (string.IsNullOrWhiteSpace(card.Type) && string.IsNullOrWhiteSpace(card.FrameType))) {
                if (_singleLookups.Add(id)) await FetchSingleCardAsync(id);
            }
            return _cache.Cards.TryGetValue(id, out card) ? card :
                new CardInfo { Id = id, Name = id.ToString(CultureInfo.InvariantCulture) };
        }
        finally { _singleLookupGate.Release(); }
    }

    public CardInfoService(ILocalStorageService localStorage, HttpClient? httpClient = null, TimeProvider? timeProvider = null) {
        _localStorage = localStorage;
        _httpClient = httpClient ?? new HttpClient();
        _timeProvider = timeProvider ?? TimeProvider.System;
        // Saved sessions already contain materialized memberships; only imports need the API/cache.
        _initializeTask = new Lazy<Task>(InitializeAsync);
    }

    public async Task<string> GetCardNameAsync(int id) {
        await _initializeTask.Value;
        await _singleLookupGate.WaitAsync();
        try {
            if (_cache.Cards.TryGetValue(id, out var cachedCard)) return cachedCard.Name;
        }
        finally { _singleLookupGate.Release(); }

        return (await GetCardInfoAsync(id)).Name;
    }

    private async Task InitializeAsync() {
        _cache = await LoadCacheAsync();
        if (IsFresh(_cache))
            return;

        await FetchAllCardsAsync();
    }

    private bool IsFresh(CardMetadataCache cache) {
        if (cache.SchemaVersion != CacheSchemaVersion || cache.LastFullRefreshUtc is not { } lastRefresh)
            return false;

        var age = _timeProvider.GetUtcNow() - lastRefresh;
        return age >= TimeSpan.Zero && age < CacheTimeToLive;
    }

    private async Task FetchAllCardsAsync() {
        try {
            using var response = await _httpClient.GetAsync(BulkApiUrl).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !TryGetProperty(document.RootElement, "data", out var data) ||
                data.ValueKind != JsonValueKind.Array) {
                return;
            }

            var cards = new Dictionary<int, CardInfo>();
            foreach (var item in data.EnumerateArray()) {
                var info = ReadCardInfo(item);
                if (info is not null) cards[info.Id] = info;
            }

            // The full YGOPRODeck catalog should not be empty; keep stale entries if a response is incomplete.
            if (cards.Count == 0)
                return;

            _cache = new CardMetadataCache {
                SchemaVersion = CacheSchemaVersion,
                LastFullRefreshUtc = _timeProvider.GetUtcNow(),
                Cards = cards
            };
            await SaveCacheAsync().ConfigureAwait(false);
        }
        catch {
            // Keep the previous snapshot as a fallback when the bulk refresh fails.
        }
    }

    private async Task FetchSingleCardAsync(int id) {
        try {
            using var response = await _httpClient.GetAsync(string.Format(CultureInfo.InvariantCulture, SingleApiTemplate, id)).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);

            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                TryGetProperty(document.RootElement, "data", out var data) &&
                data.ValueKind == JsonValueKind.Array) {
                // The query passcode remains the cache identity, including alternate passcodes.
                var info = data.EnumerateArray().Select(item => ReadCardInfo(item, id))
                    .FirstOrDefault(card => card is not null);
                if (info is not null) {
                    _cache.Cards[id] = info;
                    // Per-card enrichment is not a full catalog refresh.
                    if (_cache.SchemaVersion != CacheSchemaVersion) _cache.LastFullRefreshUtc = null;
                    _cache.SchemaVersion = CacheSchemaVersion;
                    await SaveCacheAsync().ConfigureAwait(false);
                }
            }
        }
        catch {
            // Keep imports usable by falling back to the numeric card ID.
        }
    }

    private async Task<CardMetadataCache> LoadCacheAsync() {
        try {
            var json = await _localStorage.GetRawItemAsync(CacheKey);
            if (string.IsNullOrWhiteSpace(json))
                return new CardMetadataCache();

            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return new CardMetadataCache();

            var root = document.RootElement;
            if (TryGetProperty(root, "schemaVersion", out _))
                return TryReadVersionedCache(root, out var versionedCache) ? versionedCache : new CardMetadataCache();

            return TryReadLegacyCache(root, out var legacyCache) ? legacyCache : new CardMetadataCache();
        }
        catch {
            // Malformed data and browser-storage failures must not break imports.
            return new CardMetadataCache();
        }
    }

    private static bool TryReadVersionedCache(JsonElement root, out CardMetadataCache cache) {
        cache = new CardMetadataCache();
        if (!TryGetProperty(root, "schemaVersion", out var versionProperty) ||
            versionProperty.ValueKind != JsonValueKind.Number ||
            !versionProperty.TryGetInt32(out var version) ||
            version is not (1 or CacheSchemaVersion) ||
            !TryGetProperty(root, "cards", out var cardsProperty) ||
            cardsProperty.ValueKind != JsonValueKind.Object) {
            return false;
        }

        DateTimeOffset? lastFullRefreshUtc = null;
        if (TryGetProperty(root, "lastFullRefreshUtc", out var timestampProperty) &&
            timestampProperty.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(timestampProperty.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsedTimestamp)) {
            lastFullRefreshUtc = parsedTimestamp;
        }

        var cards = new Dictionary<int, CardInfo>();
        foreach (var property in cardsProperty.EnumerateObject()) {
            if (!int.TryParse(property.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ||
                property.Value.ValueKind != JsonValueKind.Object ||
                !TryGetProperty(property.Value, "name", out var nameProperty) ||
                nameProperty.ValueKind != JsonValueKind.String) {
                return false;
            }

            var name = nameProperty.GetString();
            if (string.IsNullOrWhiteSpace(name))
                return false;

            cards[id] = version == 1 ? new CardInfo { Id = id, Name = name } : ReadCardInfo(property.Value, id)!;
        }

        if (cards.Count == 0)
            return false;

        cache = new CardMetadataCache {
            SchemaVersion = version,
            LastFullRefreshUtc = lastFullRefreshUtc,
            Cards = cards
        };
        return true;
    }

    private static bool TryReadLegacyCache(JsonElement root, out CardMetadataCache cache) {
        var cards = new Dictionary<int, CardInfo>();
        foreach (var property in root.EnumerateObject()) {
            if (!int.TryParse(property.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ||
                property.Value.ValueKind != JsonValueKind.String) {
                cache = new CardMetadataCache();
                return false;
            }

            var name = property.Value.GetString();
            if (!string.IsNullOrWhiteSpace(name))
                cards[id] = new CardInfo { Id = id, Name = name };
        }

        cache = new CardMetadataCache { SchemaVersion = 1, Cards = cards };
        return true;
    }

    private static CardInfo? ReadCardInfo(JsonElement item, int? cacheId = null) {
        if (item.ValueKind != JsonValueKind.Object) return null;
        var id = cacheId ?? Number("id");
        var name = Text("name");
        if (id is null || string.IsNullOrWhiteSpace(name)) return null;
        return new CardInfo {
            Id = id.Value, Name = name, Type = Text("type"), FrameType = Text("frameType"),
            Race = Text("race"), Attribute = Text("attribute"), Level = Number("level"),
            LinkVal = Number("linkval"), Scale = Number("scale"), Archetype = Text("archetype")
        };
        string? Text(string key) => TryGetProperty(item, key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;
        int? Number(string key) => TryGetProperty(item, key, out var value) && value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt32(out var number) ? number : null;
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement property) {
        if (element.TryGetProperty(name, out property))
            return true;

        foreach (var candidate in element.EnumerateObject()) {
            if (string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase)) {
                property = candidate.Value;
                return true;
            }
        }

        property = default;
        return false;
    }

    private async Task SaveCacheAsync() {
        try {
            await _localStorage.SetItemAsync(CacheKey, _cache);
        }
        catch {
            // The in-memory cache remains usable for the current page session.
        }
    }

    private sealed class CardMetadataCache {
        public int SchemaVersion { get; set; } = CacheSchemaVersion;
        public DateTimeOffset? LastFullRefreshUtc { get; set; }
        public Dictionary<int, CardInfo> Cards { get; set; } = new();
    }
}
