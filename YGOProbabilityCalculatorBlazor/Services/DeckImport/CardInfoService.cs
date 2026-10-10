using System.Globalization;
using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Services.DeckImport;

public class CardInfoService : ICardInfoService {
    private const string LocalCatalogUrl = "data/card-catalog.v1.json";
    private const string CardInfoApiUrl = "https://db.ygoprodeck.com/api/v7/cardinfo.php";
    private const string SingleApiTemplate = CardInfoApiUrl + "?id={0}";
    private const string CacheKey = "cardCache";
    private const int CacheSchemaVersion = 2;
    private static readonly TimeSpan CacheTimeToLive = TimeSpan.FromDays(7);
    private const int SearchResultLimit = 20;
    private const int MaximumCatalogBytes = 20 * 1024 * 1024;
    private const int MaximumCatalogCards = 30_000;
    private static readonly JsonSerializerOptions CatalogJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly ILocalStorageService _localStorage;
    private readonly TimeProvider _timeProvider;
    private readonly Lazy<Task> _loadCacheTask;
    private readonly Lazy<Task<IReadOnlyList<CardInfo>>> _loadCatalogTask;
    private CardMetadataCache _cache = new();
    private readonly SemaphoreSlim _singleLookupGate = new(1, 1);
    private readonly HashSet<int> _singleLookups = [];
    private readonly SemaphoreSlim _artworkLookupGate = new(1, 1);
    private readonly HashSet<int> _missingArtwork = [];
    private DateTimeOffset _nextArtworkLookup;

    public async Task<CardInfo> GetCardArtworkInfoAsync(int id) {
        // Artwork never initializes/refreshes the full catalog. Reuse even stale
        // validated image IDs, then enrich only a visible requested passcode.
        await _loadCacheTask.Value;

        if (_loadCatalogTask.IsValueCreated) {
            try {
                CardInfo? catalogCard = (await _loadCatalogTask.Value).FirstOrDefault(card => card.Id == id);

                if (catalogCard is not null) {
                    return catalogCard;
                }
            }
            catch {
                // A missing local catalog must not block the existing artwork fallback.
            }
        }

        // Retained identities must not queue behind another card's cold metadata.
        await _singleLookupGate.WaitAsync();

        try {
            if (_cache.Cards.TryGetValue(id, out CardInfo cached) && cached.ArtworkMetadataKnown) {
                return cached;
            }
        }
        finally {
            _singleLookupGate.Release();
        }

        await _artworkLookupGate.WaitAsync();

        try {
            if (_missingArtwork.Contains(id)) {
                return new CardInfo { Id = id, ArtworkMetadataKnown = true };
            }

            await _singleLookupGate.WaitAsync();

            try {
                if (_cache.Cards.TryGetValue(id, out CardInfo cached) && cached.ArtworkMetadataKnown) {
                    return cached;
                }
            }
            finally {
                _singleLookupGate.Release();
            }

            // Cache hits remain immediate. Space only new artwork JSON requests;
            // the metadata API's limit does not authorize image-download bursts.
            TimeSpan wait = _nextArtworkLookup - _timeProvider.GetUtcNow();

            if (wait > TimeSpan.Zero) {
                await Task.Delay(wait, _timeProvider);
            }

            _nextArtworkLookup = _timeProvider.GetUtcNow().AddMilliseconds(100);
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(15));
            using HttpResponseMessage response = await _httpClient.GetAsync(string.Format(CultureInfo.InvariantCulture, SingleApiTemplate, id), timeout.Token);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) {
                _missingArtwork.Add(id);

                return new CardInfo { Id = id, ArtworkMetadataKnown = true };
            }

            if (!response.IsSuccessStatusCode) {
                System.Net.Http.Headers.RetryConditionHeaderValue? retry = response.Headers.RetryAfter;

                throw new CardArtworkLookupException(retry?.Delta ?? (retry?.Date is DateTimeOffset date
                    ? date - _timeProvider.GetUtcNow()
                    : TimeSpan.FromMinutes(1))
                );
            }

            await using Stream stream = await response.Content.ReadAsStreamAsync();
            using JsonDocument document = await JsonDocument.ParseAsync(stream);
            JsonElement data = default;

            bool hasInvalidResponseData = document.RootElement.ValueKind != JsonValueKind.Object
                || !TryGetProperty(document.RootElement, "data", out data)
                || data.ValueKind != JsonValueKind.Array;

            if (hasInvalidResponseData) {
                throw new CardArtworkLookupException(TimeSpan.FromMinutes(1));
            }

            CardInfo? info = data.EnumerateArray().Select(item => ReadCardInfo(item, id)).FirstOrDefault(card => card is not null);

            if (info is null) {
                throw new CardArtworkLookupException(TimeSpan.FromMinutes(1));
            }

            // Do not hold the import/enrichment gate across artwork HTTP or storage.
            // Cache publication is the only shared critical section.
            await _singleLookupGate.WaitAsync();
            CardMetadataCache snapshot;

            try {
                _cache.Cards[id] = info;

                if (_cache.SchemaVersion != CacheSchemaVersion) {
                    _cache.LastFullRefreshUtc = null;
                }

                _cache.SchemaVersion = CacheSchemaVersion;

                snapshot = new() {
                    SchemaVersion = _cache.SchemaVersion,
                    LastFullRefreshUtc = _cache.LastFullRefreshUtc,
                    Cards = new(_cache.Cards)
                };
            }
            finally {
                _singleLookupGate.Release();
            }

            await SaveCacheAsync(snapshot);

            return info;
        }
        finally {
            _artworkLookupGate.Release();
        }
    }

    public async Task<IReadOnlyList<CardInfo>> SearchCardsAsync(string query) {
        IReadOnlyList<CardInfo> catalog = await _loadCatalogTask.Value;
        string normalizedQuery = query?.Trim() ?? string.Empty;

        if (normalizedQuery.Length < 2) {
            return Array.Empty<CardInfo>();
        }

        return catalog
            .Select(card => (Card: card, Rank: GetSearchRank(card.Name, normalizedQuery)))
            .Where(match => match.Rank >= 0)
            .OrderBy(match => match.Rank)
            .ThenBy(match => match.Card.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(match => match.Card.Name, StringComparer.Ordinal)
            .ThenBy(match => match.Card.Id)
            .Take(SearchResultLimit)
            .Select(match => match.Card)
            .ToArray();
    }

    public async Task<CardInfo> GetCardInfoAsync(int id) {
        await _loadCacheTask.Value;
        IReadOnlyList<CardInfo>? catalog = await TryLoadCatalogAsync();
        CardInfo? catalogCard = catalog?.FirstOrDefault(card => card.Id == id);

        if (catalogCard is not null) {
            return catalogCard;
        }

        await _singleLookupGate.WaitAsync();

        try {
            bool hasCachedCard = _cache.Cards.TryGetValue(id, out CardInfo card);
            bool needsObjectiveMetadata = !hasCachedCard
                || (string.IsNullOrWhiteSpace(card.Type) && string.IsNullOrWhiteSpace(card.FrameType))
                || !IsFresh(_cache);

            if (needsObjectiveMetadata) {
                if (_singleLookups.Add(id)) {
                    await FetchSingleCardAsync(id);
                }
            }

            return _cache.Cards.TryGetValue(id, out card)
                ? card
                : new CardInfo { Id = id, Name = id.ToString(CultureInfo.InvariantCulture) };
        }
        finally {
            _singleLookupGate.Release();
        }
    }

    public CardInfoService(ILocalStorageService localStorage, HttpClient? httpClient = null, TimeProvider? timeProvider = null) {
        _localStorage = localStorage;
        _httpClient = httpClient ?? new HttpClient();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _loadCacheTask = new Lazy<Task>(async () => _cache = await LoadCacheAsync());
        _loadCatalogTask = new Lazy<Task<IReadOnlyList<CardInfo>>>(LoadCatalogAsync);
    }

    public async Task<string> GetCardNameAsync(int id) => (await GetCardInfoAsync(id)).Name;

    public async Task<IReadOnlyDictionary<string, CardInfo>> GetCardInfoByExactNamesAsync(IEnumerable<string> names) {
        string[] requested = names
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Dictionary<string, CardInfo> resolved = new(StringComparer.Ordinal);

        if (requested.Length == 0) {
            return resolved;
        }

        await _loadCacheTask.Value;
        IReadOnlyList<CardInfo>? catalog = await TryLoadCatalogAsync();
        await _singleLookupGate.WaitAsync();

        try {
            foreach (string name in requested) {
                CardInfo[] matches = catalog is null
                    ? []
                    : [.. catalog.Where(card => card.Name.Equals(name, StringComparison.Ordinal))];

                if (matches.Length == 0) {
                    matches = [.. _cache.Cards.Values.Where(card => card.Name.Equals(name, StringComparison.Ordinal))];
                }

                if (matches.Length == 1 && HasMetadata(matches[0])) {
                    resolved[name] = matches[0];
                }
            }

            // Each unresolved name uses the exact endpoint. A bad/custom name cannot fail other lookups.
            foreach (string name in requested.Where(name => !resolved.ContainsKey(name))) {
                CardInfo? info = await FetchExactNameAsync(name);

                if (info is not null) {
                    resolved[name] = info;
                }
            }

            return resolved;
        }
        finally {
            _singleLookupGate.Release();
        }
    }

    private static bool HasMetadata(CardInfo info) => info.Id > 0 &&
        (!string.IsNullOrWhiteSpace(info.Type) || !string.IsNullOrWhiteSpace(info.FrameType)) &&
        CardPropertyProvider.GetCategories(info).Count > 0;

    private async Task<CardInfo?> FetchExactNameAsync(string name) {
        try {
            using HttpResponseMessage response = await _httpClient.GetAsync($"{CardInfoApiUrl}?name={Uri.EscapeDataString(name)}").ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);

            using JsonDocument document = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
            JsonElement data = default;
            bool hasInvalidResponseData = document.RootElement.ValueKind != JsonValueKind.Object || !TryGetProperty(document.RootElement, "data", out data) || data.ValueKind != JsonValueKind.Array;

            if (hasInvalidResponseData) {
                return null;
            }

            CardInfo?[] matches = data.EnumerateArray()
                .Select(item => ReadCardInfo(item))
                .Where(info => info is not null && info.Name.Equals(name, StringComparison.Ordinal))
                .ToArray();

            if (matches.Length != 1 || !HasMetadata(matches[0]!)) {
                return null;
            }

            CardInfo match = matches[0]!;
            _cache.Cards[match.Id] = match;

            // Exact-name enrichment also is not a full catalog refresh.
            if (_cache.SchemaVersion != CacheSchemaVersion) {
                _cache.LastFullRefreshUtc = null;
            }

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
        if (cache.SchemaVersion != CacheSchemaVersion || cache.LastFullRefreshUtc is not DateTimeOffset lastRefresh) {
            return false;
        }

        TimeSpan age = _timeProvider.GetUtcNow() - lastRefresh;

        return age >= TimeSpan.Zero && age < CacheTimeToLive;
    }

    private async Task<IReadOnlyList<CardInfo>?> TryLoadCatalogAsync() {
        try {
            return await _loadCatalogTask.Value;
        }
        catch {
            // Imported-card and legacy-name paths retain their targeted API fallback.
            return null;
        }
    }

    private async Task<IReadOnlyList<CardInfo>> LoadCatalogAsync() {
        using HttpResponseMessage response = await _httpClient.GetAsync(LocalCatalogUrl);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is > MaximumCatalogBytes) {
            throw new InvalidDataException("The local card catalog exceeds the supported size.");
        }

        await using Stream stream = await response.Content.ReadAsStreamAsync();
        byte[] json = await ReadCatalogBytesAsync(stream);
        CatalogDocument? document = JsonSerializer.Deserialize<CatalogDocument>(json, CatalogJsonOptions);

        if (document?.Version != 1 || document.Cards is null || document.Cards.Length is 0 or > MaximumCatalogCards) {
            throw new InvalidDataException("The local card catalog is empty, oversized, or unsupported.");
        }

        HashSet<int> ids = new();

        foreach (CardInfo card in document.Cards) {
            if (card.Id <= 0 || string.IsNullOrWhiteSpace(card.Name) || !card.ArtworkMetadataKnown || !ids.Add(card.Id)) {
                throw new InvalidDataException("The local card catalog contains an invalid card record.");
            }
        }

        return document.Cards;
    }

    private static async Task<byte[]> ReadCatalogBytesAsync(Stream stream) {
        using MemoryStream buffer = new();
        byte[] chunk = new byte[81_920];
        int read;

        while ((read = await stream.ReadAsync(chunk)) > 0) {
            if (buffer.Length + read > MaximumCatalogBytes) {
                throw new InvalidDataException("The local card catalog exceeds the supported size.");
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read));
        }

        if (buffer.Length == 0) {
            throw new InvalidDataException("The local card catalog is empty.");
        }

        return buffer.ToArray();
    }

    private static int GetSearchRank(string name, string query) {
        if (name.Equals(query, StringComparison.OrdinalIgnoreCase)) {
            return 0;
        }

        if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase)) {
            return 1;
        }

        return name.Contains(query, StringComparison.OrdinalIgnoreCase) ? 2 : -1;
    }

    private async Task FetchSingleCardAsync(int id) {
        try {
            using HttpResponseMessage response = await _httpClient.GetAsync(string.Format(CultureInfo.InvariantCulture, SingleApiTemplate, id)).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using JsonDocument document = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
            JsonElement data = default;

            bool hasValidResponseData = document.RootElement.ValueKind == JsonValueKind.Object
                && TryGetProperty(document.RootElement, "data", out data)
                && data.ValueKind == JsonValueKind.Array;

            if (hasValidResponseData) {
                // The query passcode remains the cache identity, including alternate passcodes.
                CardInfo? info = data.EnumerateArray().Select(item => ReadCardInfo(item, id)).FirstOrDefault(card => card is not null);

                if (info is not null) {
                    _cache.Cards[id] = info;

                    // Per-card enrichment is not a full catalog refresh.
                    if (_cache.SchemaVersion != CacheSchemaVersion) {
                        _cache.LastFullRefreshUtc = null;
                    }

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
            string? json = await _localStorage.GetRawItemAsync(CacheKey);

            if (string.IsNullOrWhiteSpace(json)) {
                return new CardMetadataCache();
            }

            using JsonDocument document = JsonDocument.Parse(json);

            if (document.RootElement.ValueKind != JsonValueKind.Object) {
                return new CardMetadataCache();
            }

            JsonElement root = document.RootElement;

            if (TryGetProperty(root, "schemaVersion", out _)) {
                return TryReadVersionedCache(root, out CardMetadataCache versionedCache)
                    ? versionedCache
                    : new CardMetadataCache();
            }

            return TryReadLegacyCache(root, out CardMetadataCache legacyCache)
                ? legacyCache
                : new CardMetadataCache();
        }
        catch {
            // Malformed data and browser-storage failures must not break imports.
            return new CardMetadataCache();
        }
    }

    private static bool TryReadVersionedCache(JsonElement root, out CardMetadataCache cache) {
        cache = new CardMetadataCache();
        JsonElement cardsProperty = default;
        int version = 0;

        bool hasInvalidCacheHeader = !TryGetProperty(root, "schemaVersion", out JsonElement versionProperty)
            || versionProperty.ValueKind != JsonValueKind.Number
            || !versionProperty.TryGetInt32(out version)
            || version is not (1 or CacheSchemaVersion)
            || !TryGetProperty(root, "cards", out cardsProperty)
            || cardsProperty.ValueKind != JsonValueKind.Object;

        if (hasInvalidCacheHeader) {
            return false;
        }

        DateTimeOffset? lastFullRefreshUtc = null;
        DateTimeOffset parsedTimestamp = default;

        bool hasValidTimestamp = TryGetProperty(root, "lastFullRefreshUtc", out JsonElement timestampProperty)
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
                || !TryGetProperty(property.Value, "name", out JsonElement nameProperty)
                || nameProperty.ValueKind != JsonValueKind.String) {

                return false;
            }

            string? name = nameProperty.GetString();

            if (string.IsNullOrWhiteSpace(name)) {
                return false;
            }

            cards[id] = version == 1 ? new CardInfo { Id = id, Name = name } : ReadCardInfo(property.Value, id, fromCache: true)!;
        }

        if (cards.Count == 0) {
            return false;
        }

        cache = new() {
            SchemaVersion = version,
            LastFullRefreshUtc = lastFullRefreshUtc,
            Cards = cards
        };

        return true;
    }

    private static bool TryReadLegacyCache(JsonElement root, out CardMetadataCache cache) {
        Dictionary<int, CardInfo> cards = new();

        foreach (JsonProperty property in root.EnumerateObject()) {
            if (!int.TryParse(property.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) ||
                property.Value.ValueKind != JsonValueKind.String) {
                cache = new CardMetadataCache();

                return false;
            }

            string? name = property.Value.GetString();

            if (!string.IsNullOrWhiteSpace(name)) {
                cards[id] = new CardInfo { Id = id, Name = name };
            }
        }

        cache = new() { SchemaVersion = 1, Cards = cards };

        return true;
    }

    private static CardInfo? ReadCardInfo(JsonElement item, int? cacheId = null, bool fromCache = false) {
        if (item.ValueKind != JsonValueKind.Object) {
            return null;
        }

        int? id = cacheId ?? Number("id");
        string? name = Text("name");

        if (id is null || string.IsNullOrWhiteSpace(name)) {
            return null;
        }

        return new CardInfo {
            Id = id.Value,
            Name = name,
            Type = Text("type"),
            FrameType = Text("frameType"),
            Race = Text("race"),
            Attribute = Text("attribute"),
            Level = Number("level"),
            LinkVal = Number("linkval"),
            Scale = Number("scale"),
            Archetype = Text("archetype"),
            CanonicalCardId = fromCache ? Number("canonicalCardId") : Number("id"),
            ArtworkImageIds = ReadImageIds(),
            ArtworkMetadataKnown = !fromCache || (TryGetProperty(item, "artworkMetadataKnown", out JsonElement known) && known.ValueKind == JsonValueKind.True)
        };

        IReadOnlyList<int> ReadImageIds() {
            JsonElement images;
            bool hasArtworkImages = TryGetProperty(item, fromCache ? "artworkImageIds" : "card_images", out images) && images.ValueKind == JsonValueKind.Array;

            if (!hasArtworkImages) {
                return Array.Empty<int>();
            }

            List<int> result = new();

            foreach (JsonElement image in images.EnumerateArray()) {
                int imageId = default;

                if (fromCache) {
                    if (image.ValueKind != JsonValueKind.Number || !image.TryGetInt32(out imageId)) {
                        continue;
                    }
                }
                else {
                    bool hasValidImageId = image.ValueKind == JsonValueKind.Object &&
                        TryGetProperty(image, "id", out JsonElement imageIdValue) &&
                        imageIdValue.ValueKind == JsonValueKind.Number && imageIdValue.TryGetInt32(out imageId);

                    if (!hasValidImageId) {
                        continue;
                    }

                    bool hasExpectedImageUrl = TryGetProperty(image, "image_url_small", out JsonElement url) &&
                        url.ValueKind == JsonValueKind.String &&
                        url.GetString() == $"https://images.ygoprodeck.com/images/cards_small/{imageId}.jpg";

                    if (!hasExpectedImageUrl) {
                        continue;
                    }
                }

                if (imageId is > 0 and <= 2147483647 && !result.Contains(imageId)) {
                    result.Add(imageId);
                }
            }

            return result.Count == 0 ? Array.Empty<int>() : result.AsReadOnly();
        }

        string? Text(string key) => TryGetProperty(item, key, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

        int? Number(string key) => TryGetProperty(item, key, out JsonElement value) && value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt32(out int number)
                ? number
                : null;
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement property) {
        if (element.TryGetProperty(name, out property)) {
            return true;
        }

        foreach (JsonProperty candidate in element.EnumerateObject()) {
            if (string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase)) {
                property = candidate.Value;
                return true;
            }
        }

        property = default;

        return false;
    }

    private async Task SaveCacheAsync(CardMetadataCache? snapshot = null) {
        try {
            await _localStorage.SetItemAsync(CacheKey, snapshot ?? _cache);
        }
        catch {
            // The in-memory cache remains usable for the current page session.
        }
    }

    private sealed class CatalogDocument {
        public CatalogDocument() { }

        public int Version { get; init; }
        public CardInfo[] Cards { get; init; } = [];
    }

    private sealed class CardMetadataCache {
        public int SchemaVersion { get; set; } = CacheSchemaVersion;
        public DateTimeOffset? LastFullRefreshUtc { get; set; }
        public Dictionary<int, CardInfo> Cards { get; set; } = new();
    }
}
