using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Services.DeckImport;

public class CardInfoService : ICardInfoService {
    private const string BulkApiUrl = "https://db.ygoprodeck.com/api/v7/cardinfo.php";
    private const string SingleApiTemplate = "https://db.ygoprodeck.com/api/v7/cardinfo.php?id={0}";
    private const string CacheFileName = "cardcache.json";

    private readonly HttpClient _httpClient = new();
    private readonly Dictionary<int, string> _cache;
    private readonly IFileService _fileService;
    private readonly ISerializer _serializer;

    public CardInfoService(IFileService fileService, ISerializer serializer) {
        _fileService = fileService;
        _serializer = serializer;
        _cache = LoadCache();

        if (_cache.Count == 0)
            _ = FetchAllCardsAsync();
    }

    public async Task<string> GetCardNameAsync(int id) {
        if (_cache.TryGetValue(id, out var name))
            return name;

        await FetchSingleCardAsync(id);

        return _cache.TryGetValue(id, out name) ? name : id.ToString();
    }

    private async Task FetchAllCardsAsync() {
        try {
            var res = await _httpClient.GetAsync(BulkApiUrl).ConfigureAwait(false);
            res.EnsureSuccessStatusCode();

            await using var stream = await res.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);

            if (doc.RootElement.TryGetProperty("data", out var array)) {
                foreach (var item in array.EnumerateArray()) {
                    if (item.TryGetProperty("id", out var idProp) &&
                        item.TryGetProperty("name", out var nameProp) &&
                        idProp.TryGetInt32(out var cid)) {
                        _cache[cid] = nameProp.GetString() ?? cid.ToString();
                    }
                }

                await SaveCacheAsync();
            }
        }
        catch {
            // ignored
        }
    }

    private async Task FetchSingleCardAsync(int id) {
        try {
            var res = await _httpClient.GetAsync(string.Format(SingleApiTemplate, id)).ConfigureAwait(false);
            res.EnsureSuccessStatusCode();

            await using var stream = await res.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);

            if (doc.RootElement.TryGetProperty("data", out var array) &&
                array.GetArrayLength() > 0) {
                var item = array[0];
                if (item.TryGetProperty("name", out var nameProp)) {
                    _cache[id] = nameProp.GetString() ?? id.ToString();
                    await SaveCacheAsync();
                }
            }
        }
        catch {
            // ignored
        }
    }

    private Dictionary<int, string> LoadCache() {
        try {
            var json = _fileService.ReadAllTextAsync(CacheFileName).GetAwaiter().GetResult();
            return _serializer.Deserialize<Dictionary<int, string>>(json)
                   ?? new Dictionary<int, string>();
        }
        catch {
            // ignored
        }

        return new Dictionary<int, string>();
    }

    private async Task SaveCacheAsync() {
        try {
            var json = _serializer.Serialize(_cache);
            await _fileService.WriteAllTextAsync(CacheFileName, json);
        }
        catch {
            // ignored
        }
    }
}
