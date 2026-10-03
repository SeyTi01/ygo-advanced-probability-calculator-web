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
    private readonly Lazy<Task> _loadCacheTask;
    private CardMetadataCache _cache = new();
    private readonly SemaphoreSlim _singleLookupGate = new(1, 1);
    private readonly HashSet<int> _singleLookups = [];
    private readonly HashSet<int> _artworkLookups = [];

    public async Task<CardInfo> GetCardArtworkInfoAsync(int id) {
        var info = await GetCardInfoAsync(id);
        await _singleLookupGate.WaitAsync();
        try {
            // Fresh pre-artwork v2 snapshots remain valid for import/properties. Enrich only
            // a requested preview, at most once per ID per service lifetime on failure.
            if (_cache.Cards.TryGetValue(id, out info) && !info.ArtworkMetadataKnown &&
                _artworkLookups.Add(id) && !_singleLookups.Contains(id))
                await FetchSingleCardAsync(id);
            return _cache.Cards.TryGetValue(id, out info) ? info : new CardInfo { Id = id };
        }
        finally { _singleLookupGate.Release(); }
    }

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
        // Fully enriched sessions never need a lookup; legacy sessions can resolve exact names lazily.
        _loadCacheTask = new Lazy<Task>(async () => _cache = await LoadCacheAsync());
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
        await _loadCacheTask.Value;
        await _singleLookupGate.WaitAsync();
        try {
            if (!IsFresh(_cache)) await FetchAllCardsAsync();
        }
        finally { _singleLookupGate.Release(); }
    }

    public async Task<IReadOnlyDictionary<string, CardInfo>> GetCardInfoByExactNamesAsync(IEnumerable<string> names) {
        var requested = names.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.Ordinal).ToArray();
        var resolved = new Dictionary<string, CardInfo>(StringComparer.Ordinal);
        if (requested.Length == 0) return resolved;

        await _loadCacheTask.Value;
        await _singleLookupGate.WaitAsync();
        try {
            foreach (var name in requested) {
                var matches = _cache.Cards.Values.Where(card => card.Name.Equals(name, StringComparison.Ordinal)).ToArray();
                if (matches.Length == 1 && HasMetadata(matches[0])) resolved[name] = matches[0];
            }

            // Each unresolved name uses the exact endpoint. A bad/custom name cannot fail other lookups.
            foreach (var name in requested.Where(name => !resolved.ContainsKey(name))) {
                var info = await FetchExactNameAsync(name);
                if (info is not null) resolved[name] = info;
            }
            return resolved;
        }
        finally { _singleLookupGate.Release(); }
    }

    private static bool HasMetadata(CardInfo info) => info.Id > 0 &&
        (!string.IsNullOrWhiteSpace(info.Type) || !string.IsNullOrWhiteSpace(info.FrameType)) &&
        CardPropertyProvider.GetCategories(info).Count > 0;

    private async Task<CardInfo?> FetchExactNameAsync(string name) {
        try {
            using var response = await _httpClient.GetAsync($"{BulkApiUrl}?name={Uri.EscapeDataString(name)}").ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !TryGetProperty(document.RootElement, "data", out var data) || data.ValueKind != JsonValueKind.Array)
                return null;

            var matches = data.EnumerateArray().Select(item => ReadCardInfo(item))
                .Where(info => info is not null && info.Name.Equals(name, StringComparison.Ordinal)).ToArray();
            if (matches.Length != 1 || !HasMetadata(matches[0]!)) return null;
            var match = matches[0]!;
            _cache.Cards[match.Id] = match;
            // Exact-name enrichment also is not a full catalog refresh.
            if (_cache.SchemaVersion != CacheSchemaVersion) _cache.LastFullRefreshUtc = null;
            _cache.SchemaVersion = CacheSchemaVersion;
            await SaveCacheAsync().ConfigureAwait(false);
            return match;
        }
        catch {
            // Valid legacy sessions remain loadable offline and when a name cannot be resolved.
            return null;
        }
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

            cards[id] = version == 1 ? new CardInfo { Id = id, Name = name } : ReadCardInfo(property.Value, id, fromCache: true)!;
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

    private static CardInfo? ReadCardInfo(JsonElement item, int? cacheId = null, bool fromCache = false) {
        if (item.ValueKind != JsonValueKind.Object) return null;
        var id = cacheId ?? Number("id");
        var name = Text("name");
        if (id is null || string.IsNullOrWhiteSpace(name)) return null;
        return new CardInfo {
            Id = id.Value, Name = name, Type = Text("type"), FrameType = Text("frameType"),
            Race = Text("race"), Attribute = Text("attribute"), Level = Number("level"),
            LinkVal = Number("linkval"), Scale = Number("scale"), Archetype = Text("archetype"),
            CanonicalCardId = fromCache ? Number("canonicalCardId") : Number("id"),
            ArtworkImageIds = ReadImageIds(),
            ArtworkMetadataKnown = !fromCache || (TryGetProperty(item, "artworkMetadataKnown", out var known) &&
                known.ValueKind == JsonValueKind.True)
        };
        IReadOnlyList<int> ReadImageIds() {
            if (!TryGetProperty(item, fromCache ? "artworkImageIds" : "card_images", out var images) ||
                images.ValueKind != JsonValueKind.Array) return Array.Empty<int>();
            var result = new List<int>();
            foreach (var image in images.EnumerateArray()) {
                int imageId;
                if (fromCache) {
                    if (image.ValueKind != JsonValueKind.Number || !image.TryGetInt32(out imageId)) continue;
                }
                else {
                    if (image.ValueKind != JsonValueKind.Object || !TryGetProperty(image, "id", out var imageIdValue) ||
                        imageIdValue.ValueKind != JsonValueKind.Number || !imageIdValue.TryGetInt32(out imageId) ||
                        !TryGetProperty(image, "image_url_small", out var url) || url.ValueKind != JsonValueKind.String ||
                        url.GetString() != $"https://images.ygoprodeck.com/images/cards_small/{imageId}.jpg") continue;
                }
                if (imageId is > 0 and <= 2147483647 && !result.Contains(imageId)) result.Add(imageId);
            }
            return result.Count == 0 ? Array.Empty<int>() : result.AsReadOnly();
        }
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
