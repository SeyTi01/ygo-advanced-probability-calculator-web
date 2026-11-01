using System.Globalization;
using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Services.DeckImport;

public class CardInfoService : ICardInfoService {
    private const string CardInfoApiUrl = "https://db.ygoprodeck.com/api/v7/cardinfo.php";
    private const string SingleApiTemplate = CardInfoApiUrl + "?id={0}";
    // Register only reviewed, deployed name assets. English needs no additional data.
    private static readonly IReadOnlyDictionary<string, string> LocalizedNameAssets = new Dictionary<string, string> {
        ["de"] = "data/card-names.de.v1.json",
        ["ja"] = "data/card-names.ja.v1.json",
        ["fr"] = "data/card-names.fr.v1.json",
        ["it"] = "data/card-names.it.v1.json"
    };

    private readonly HttpClient _httpClient;
    private readonly LocalCardCatalog _catalog;
    private readonly TimeProvider _timeProvider;
    private readonly CardMetadataCache _cache;
    private readonly SemaphoreSlim _singleLookupGate = new(1, 1);
    private readonly HashSet<int> _singleLookups = [];
    private readonly SemaphoreSlim _artworkLookupGate = new(1, 1);
    private readonly HashSet<int> _missingArtwork = [];
    private DateTimeOffset _nextArtworkLookup;

    public async Task<CardInfo> GetCardArtworkInfoAsync(int id) {
        // Artwork never initializes/refreshes the full catalog. Reuse even stale
        // validated image IDs, then enrich only a visible requested passcode.
        await _cache.EnsureLoadedAsync();

        CardInfo? catalogCard = await _catalog.GetLoadedCanonicalCardAsync(id);

        if (catalogCard is not null) {
            return catalogCard;
        }

        // Retained identities must not queue behind another card's cold metadata.
        await _singleLookupGate.WaitAsync();

        try {
            if (_cache.TryGet(id, out CardInfo cached) && cached.ArtworkMetadataKnown) {
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
                if (_cache.TryGet(id, out CardInfo cached) && cached.ArtworkMetadataKnown) {
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
                || !CardMetadataJson.TryGetProperty(document.RootElement, "data", out data)
                || data.ValueKind != JsonValueKind.Array;

            if (hasInvalidResponseData) {
                throw new CardArtworkLookupException(TimeSpan.FromMinutes(1));
            }

            CardInfo? info = data.EnumerateArray().Select(item => CardMetadataJson.ReadProvider(item, id)).FirstOrDefault(card => card is not null);

            if (info is null) {
                throw new CardArtworkLookupException(TimeSpan.FromMinutes(1));
            }

            // Do not hold the import/enrichment gate across artwork HTTP or storage.
            // Cache publication is the only shared critical section.
            await _singleLookupGate.WaitAsync();
            CardMetadataCache.Snapshot snapshot;

            try {
                _cache.Store(id, info);
                snapshot = _cache.CaptureSnapshot();
            }
            finally {
                _singleLookupGate.Release();
            }

            await _cache.SaveAsync(snapshot);

            return info;
        }
        finally {
            _artworkLookupGate.Release();
        }
    }

    public Task<IReadOnlyList<CardInfo>> SearchCardsAsync(string query) => _catalog.SearchCardsAsync(query);

    public string? GetLocalizedCardName(CardInfo info) => _catalog.GetLocalizedCardName(info);

    public string? GetLocalizedCardName(Card card) => _catalog.GetLocalizedCardName(card);

    public Task EnsureLocalizedCardNamesAsync(IReadOnlyList<Card> cards) => _catalog.EnsureLocalizedCardNamesAsync(cards);

    public async Task<CardInfo> GetCardInfoAsync(int id) {
        await _cache.EnsureLoadedAsync();
        await _catalog.TryLoadAsync();
        CardInfo? catalogCard = _catalog.Find(id);

        if (catalogCard is not null) {
            return catalogCard.Id == id ? catalogCard : catalogCard with { Id = id };
        }

        await _singleLookupGate.WaitAsync();

        try {
            bool hasCachedCard = _cache.TryGet(id, out CardInfo card);
            bool needsObjectiveMetadata = !hasCachedCard
                || (string.IsNullOrWhiteSpace(card.Type) && string.IsNullOrWhiteSpace(card.FrameType))
                || !_cache.IsFresh;

            if (needsObjectiveMetadata) {
                if (_singleLookups.Add(id)) {
                    await FetchSingleCardAsync(id);
                }
            }

            return _cache.TryGet(id, out card)
                ? WithCatalogMembership(card)
                : new CardInfo { Id = id, Name = id.ToString(CultureInfo.InvariantCulture) };
        }
        finally {
            _singleLookupGate.Release();
        }
    }

    public CardInfoService(ILocalStorageService localStorage, HttpClient? httpClient = null, TimeProvider? timeProvider = null): this(localStorage, httpClient, timeProvider, LocalizedNameAssets) { }

    internal CardInfoService(
        ILocalStorageService localStorage,
        HttpClient? httpClient,
        TimeProvider? timeProvider,
        IReadOnlyDictionary<string, string> localizedNameAssets
    ) {
        _httpClient = httpClient ?? new HttpClient();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _cache = new(localStorage, _timeProvider);
        _catalog = new(_httpClient, localizedNameAssets);
    }

    public async Task<IReadOnlyList<CardInfo>> GetCatalogCardsAsync() => await _catalog.TryLoadAsync() ?? [];

    private CardInfo WithCatalogMembership(CardInfo info) {
        CardInfo? canonical = _catalog.Find(info.CanonicalCardId ?? info.Id);

        return canonical is null ? info : info with {
            ResolvedArchetypes = canonical.ResolvedArchetypes,
            ResolvedNames = canonical.ResolvedNames,
            MentionedNames = canonical.MentionedNames
        };
    }

    public async Task<IReadOnlyDictionary<string, CardInfo>> GetCardInfoByExactNamesAsync(IEnumerable<string> names) {
        string[] requested = names
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Dictionary<string, CardInfo> resolved = new(StringComparer.Ordinal);

        if (requested.Length == 0) {
            return resolved;
        }

        await _cache.EnsureLoadedAsync();
        IReadOnlyList<CardInfo>? catalog = await _catalog.TryLoadAsync();
        await _singleLookupGate.WaitAsync();

        try {
            foreach (string name in requested) {
                CardInfo[] matches = catalog is null
                    ? []
                    : [.. catalog.Where(card => card.Name.Equals(name, StringComparison.Ordinal))];

                if (matches.Length == 0) {
                    matches = [.. _cache.Cards.Where(card => card.Name.Equals(name, StringComparison.Ordinal))];
                }

                if (matches.Length == 1 && HasMetadata(matches[0])) {
                    resolved[name] = WithCatalogMembership(matches[0]);
                }
            }

            // Each unresolved name uses the exact endpoint. A bad/custom name cannot fail other lookups.
            foreach (string name in requested.Where(name => !resolved.ContainsKey(name))) {
                CardInfo? info = await FetchExactNameAsync(name);

                if (info is not null) {
                    resolved[name] = WithCatalogMembership(info);
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
            bool hasInvalidResponseData = document.RootElement.ValueKind != JsonValueKind.Object || !CardMetadataJson.TryGetProperty(document.RootElement, "data", out data) || data.ValueKind != JsonValueKind.Array;

            if (hasInvalidResponseData) {
                return null;
            }

            CardInfo?[] matches = data.EnumerateArray()
                .Select(item => CardMetadataJson.ReadProvider(item))
                .Where(info => info is not null && info.Name.Equals(name, StringComparison.Ordinal))
                .ToArray();

            if (matches.Length != 1 || !HasMetadata(matches[0]!)) {
                return null;
            }

            CardInfo match = matches[0]!;
            _cache.Store(match.Id, match);
            await _cache.SaveAsync().ConfigureAwait(false);

            return match;
        }
        catch {
            // Valid legacy sessions remain loadable offline and when a name cannot be resolved.
            return null;
        }
    }

    private async Task FetchSingleCardAsync(int id) {
        try {
            using HttpResponseMessage response = await _httpClient.GetAsync(string.Format(CultureInfo.InvariantCulture, SingleApiTemplate, id)).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using JsonDocument document = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
            JsonElement data = default;

            bool hasValidResponseData = document.RootElement.ValueKind == JsonValueKind.Object
                && CardMetadataJson.TryGetProperty(document.RootElement, "data", out data)
                && data.ValueKind == JsonValueKind.Array;

            if (hasValidResponseData) {
                // The query passcode remains the cache identity, including alternate passcodes.
                CardInfo? info = data.EnumerateArray().Select(item => CardMetadataJson.ReadProvider(item, id)).FirstOrDefault(card => card is not null);

                if (info is not null) {
                    _cache.Store(id, info);
                    await _cache.SaveAsync().ConfigureAwait(false);
                }
            }
        }
        catch {
            // Keep imports usable by falling back to the numeric card ID.
        }
    }
}
