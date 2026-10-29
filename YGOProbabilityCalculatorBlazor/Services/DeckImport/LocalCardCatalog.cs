using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.DeckImport;

// Owns same-origin catalog identity and optional display data. Provider cache and
// external metadata requests cannot publish identities into this catalog.
internal sealed class LocalCardCatalog {
    private const string LocalCatalogUrl = "data/card-catalog.v1.json";
    private const int SearchResultLimit = 20;
    private const int MaximumCatalogBytes = 20 * 1024 * 1024;
    private const int MaximumCatalogCards = 30_000;
    private const int MaximumLocalizedNamesBytes = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions CatalogJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly IReadOnlyDictionary<string, string> _localizedNameAssets;
    private readonly Lazy<Task<IReadOnlyList<CardInfo>>> _loadCatalogTask;
    private readonly ConcurrentDictionary<string, Lazy<Task<IReadOnlyDictionary<CardInfo, string>>>> _localizedNameLoads = new();
    private CardCatalogIndex? _catalogIndex;

    public LocalCardCatalog(HttpClient httpClient, IReadOnlyDictionary<string, string> localizedNameAssets) {
        _httpClient = httpClient;
        _localizedNameAssets = localizedNameAssets;
        _loadCatalogTask = new(LoadCatalogAsync);
    }

    public CardInfo? Find(int id) => _catalogIndex?.Find(id);

    // Artwork may await an existing catalog request but never starts one.
    public async Task<CardInfo?> GetLoadedCanonicalCardAsync(int id) {
        if (!_loadCatalogTask.IsValueCreated) {
            return null;
        }

        try {
            await _loadCatalogTask.Value;
            return _catalogIndex!.CanonicalCards.GetValueOrDefault(id);
        }
        catch {
            // A missing local catalog must not block the existing artwork fallback.
            return null;
        }
    }

    public async Task<IReadOnlyList<CardInfo>> SearchCardsAsync(string query) {
        string normalizedQuery = query?.Trim() ?? string.Empty;

        if (normalizedQuery.Length < 2) {
            return Array.Empty<CardInfo>();
        }

        Task<IReadOnlyList<CardInfo>> catalogTask = _loadCatalogTask.Value;
        string? locale = GetNameLocale();
        Task<IReadOnlyDictionary<CardInfo, string>>? namesTask = locale is null ? null
            : GetLocalizedNamesAsync(locale);
        IReadOnlyList<CardInfo> catalog = await catalogTask;
        if (namesTask is not null) {
            IReadOnlyDictionary<CardInfo, string> names = await namesTask;
            if (names.Count > 0) {
                return catalog
                    .Select(card => (Card: card, Rank: Math.Min(SearchRank(card.Name, normalizedQuery),
                        names.TryGetValue(card, out string name) ? SearchRank(name, normalizedQuery) : 3)))
                    .Where(match => match.Rank < 3)
                    .OrderBy(match => match.Rank)
                    .ThenBy(match => match.Card.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(match => match.Card.Name, StringComparer.Ordinal)
                    .ThenBy(match => match.Card.Id)
                    .Take(SearchResultLimit)
                    .Select(match => match.Card)
                    .ToArray();
            }
        }
        // The secondary order is prepared once. Exact matches precede other
        // prefixes in this order; locate their range without scanning the catalog.
        int lower = 0;
        int upper = catalog.Count;

        while (lower < upper) {
            int middle = lower + (upper - lower) / 2;
            if (StringComparer.OrdinalIgnoreCase.Compare(catalog[middle].Name, normalizedQuery) < 0) {
                lower = middle + 1;
            }
            else {
                upper = middle;
            }
        }

        List<CardInfo> matches = new(SearchResultLimit);
        for (int index = lower; index < catalog.Count && matches.Count < SearchResultLimit; index++) {
            CardInfo card = catalog[index];
            if (!card.Name.StartsWith(normalizedQuery, StringComparison.OrdinalIgnoreCase)) {
                break;
            }

            matches.Add(card);
        }

        // Fill only the remaining slots with substrings in secondary order.
        // Broad prefix queries never scan; broad substrings stop at the 20th hit.
        foreach (CardInfo card in catalog) {
            if (matches.Count == SearchResultLimit) {
                break;
            }

            if (card.Name.IndexOf(normalizedQuery, StringComparison.OrdinalIgnoreCase) > 0) {
                matches.Add(card);
            }
        }

        return matches.ToArray();
    }

    private string? GetNameLocale() {
        for (CultureInfo culture = CultureInfo.CurrentUICulture; culture.Name.Length > 0; culture = culture.Parent) {
            if (_localizedNameAssets.ContainsKey(culture.Name)) {
                return culture.Name;
            }
        }

        return null;
    }

    private static int SearchRank(string name, string query) {
        if (name.Equals(query, StringComparison.OrdinalIgnoreCase)) {
            return 0;
        }

        if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase)) {
            return 1;
        }

        return name.Contains(query, StringComparison.OrdinalIgnoreCase) ? 2 : 3;
    }

    // Synchronous label reads never start a request.
    public string? GetLocalizedCardName(CardInfo info) {
        string? locale = GetNameLocale();
        if (locale is not null && _localizedNameLoads.TryGetValue(locale, out Lazy<Task<IReadOnlyDictionary<CardInfo, string>>> load)
            && load.IsValueCreated && load.Value.IsCompletedSuccessfully) {
            // Only the original verified catalog object receives a data label. A live/cache
            // object with the same ID or an artwork alias is not identity evidence here.
            return load.Value.Result.TryGetValue(info, out string name) ? name : null;
        }
        return null;
    }

    public string? GetLocalizedCardName(Card card) {
        if (GetNameLocale() is null || !_loadCatalogTask.IsValueCreated || !_loadCatalogTask.Value.IsCompletedSuccessfully) {
            return null;
        }
        CardInfo? canonical = FindSavedCardForDisplay(card);
        return canonical is null ? null : GetLocalizedCardName(canonical);
    }

    public async Task EnsureLocalizedCardNamesAsync(IReadOnlyList<Card> cards) {
        string? locale = GetNameLocale();
        if (locale is null || !cards.Any(card => card.ExternalCardId is > 0 && !string.IsNullOrWhiteSpace(card.Name))) {
            return;
        }

        // Start both same-origin requests together. Once the catalog is warm, custom
        // or unknown entries can be rejected without requesting an unused name asset.
        Task<IReadOnlyList<CardInfo>> catalog = _loadCatalogTask.Value;
        if (catalog.IsCompletedSuccessfully && !cards.Any(card => FindSavedCardForDisplay(card) is not null)) {
            return;
        }

        try {
            await Task.WhenAll(catalog, GetLocalizedNamesAsync(locale));
        }
        catch {
            // Optional display preparation must leave the stored names usable.
        }
    }

    private CardInfo? FindSavedCardForDisplay(Card card) {
        if (card.ExternalCardId is not > 0 || string.IsNullOrWhiteSpace(card.Name)) {
            return null;
        }
        CardInfo? canonical = _catalogIndex?.Find(card.ExternalCardId.Value);
        return canonical is not null && string.Equals(card.Name, canonical.Name, StringComparison.Ordinal)
            ? canonical : null;
    }

    private Task<IReadOnlyDictionary<CardInfo, string>> GetLocalizedNamesAsync(string locale) =>
        _localizedNameLoads.GetOrAdd(locale, nameLocale => new(() => LoadLocalizedNamesAsync(nameLocale))).Value;

    private async Task<IReadOnlyDictionary<CardInfo, string>> LoadLocalizedNamesAsync(string locale) {
        try {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
            using HttpResponseMessage response = await _httpClient.GetAsync(_localizedNameAssets[locale], timeout.Token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > MaximumLocalizedNamesBytes) {
                throw new InvalidDataException("The localized card names exceed the supported size.");
            }
            await using Stream stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            byte[] json = await ReadCatalogBytesAsync(stream, MaximumLocalizedNamesBytes, timeout.Token);
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.GetProperty("version").GetInt32() != 1 || root.GetProperty("locale").GetString() != locale) {
                throw new InvalidDataException("Unsupported localized card names.");
            }
            JsonElement entries = root.GetProperty("names");
            if (entries.ValueKind != JsonValueKind.Array || entries.GetArrayLength() > MaximumCatalogCards) {
                throw new InvalidDataException("Invalid localized card names.");
            }
            await _loadCatalogTask.Value;
            IReadOnlyDictionary<int, CardInfo> canonicalCards = _catalogIndex!.CanonicalCards;
            HashSet<int> seen = [];
            Dictionary<CardInfo, string> names = new(ReferenceEqualityComparer.Instance);
            foreach (JsonElement entry in entries.EnumerateArray()) {
                if (entry.ValueKind != JsonValueKind.Array || entry.GetArrayLength() != 2) {
                    throw new InvalidDataException("Invalid localized name record.");
                }
                int id = entry[0].GetInt32();
                string? name = entry[1].GetString();
                if (id <= 0 || !seen.Add(id) || string.IsNullOrWhiteSpace(name) || name.Length > 512) {
                    throw new InvalidDataException("Invalid localized name identity.");
                }
                // Only exact canonical IDs join. Artwork IDs and names never establish identity.
                if (canonicalCards.TryGetValue(id, out CardInfo card)) {
                    names.Add(card, name);
                }
            }
            return names;
        }
        catch {
            // An optional asset failure leaves English search and manual entry usable.
            // Cache the failure for this service lifetime to avoid repeated failed requests.
        }
        return new Dictionary<CardInfo, string>(ReferenceEqualityComparer.Instance);
    }

    public async Task<IReadOnlyList<CardInfo>?> TryLoadAsync() {
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

        if (document?.Version is not (1 or 2) || document.Cards is null || document.Cards.Length is 0 or > MaximumCatalogCards) {
            throw new InvalidDataException("The local card catalog is empty, oversized, or unsupported.");
        }

        HashSet<int> ids = new();

        foreach (CardInfo card in document.Cards) {
            if (card.Id <= 0 || string.IsNullOrWhiteSpace(card.Name) || card.Name.Length > 512
                || !card.ArtworkMetadataKnown || card.ArtworkImageIds is null || card.ArtworkImageIds.Count > 100
                || card.ArtworkImageIds.Any(id => id <= 0) || !ids.Add(card.Id)) {
                throw new InvalidDataException("The local card catalog contains an invalid card record.");
            }
            foreach (IReadOnlyList<string>? values in new[] { card.ResolvedNames, card.MentionedNames }) {
                if (values is not null && (values.Count > 32 || values.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 512)
                    || values.Distinct(StringComparer.Ordinal).Count() != values.Count)) {
                    throw new InvalidDataException("The local card catalog contains invalid name metadata.");
                }
            }
            if (card.ResolvedArchetypes is { } memberships && (memberships.Count > 32
                || memberships.Any(name => string.IsNullOrWhiteSpace(name) || name.Length > 100)
                || memberships.Distinct(StringComparer.OrdinalIgnoreCase).Count() != memberships.Count)) {
                throw new InvalidDataException("The local card catalog contains invalid archetype memberships.");
            }
        }

        CardInfo[] cards = document.Cards
            .Select(card => document.Version == 1 ? card with { ResolvedArchetypes = null } : card)
            .OrderBy(card => card.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(card => card.Name, StringComparer.Ordinal)
            .ThenBy(card => card.Id)
            .ToArray();
        _catalogIndex = new(cards);

        return cards;
    }

    private static async Task<byte[]> ReadCatalogBytesAsync(
        Stream stream,
        int maximumBytes = MaximumCatalogBytes,
        CancellationToken cancellationToken = default
    ) {
        using MemoryStream buffer = new();
        byte[] chunk = new byte[81_920];
        int read;

        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0) {
            if (buffer.Length + read > maximumBytes) {
                throw new InvalidDataException("The local card catalog exceeds the supported size.");
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read));
        }

        if (buffer.Length == 0) {
            throw new InvalidDataException("The local card catalog is empty.");
        }

        return buffer.ToArray();
    }

    private sealed class CatalogDocument {
        public CatalogDocument() { }

        public int Version { get; init; }
        public CardInfo[] Cards { get; init; } = [];
    }
}
