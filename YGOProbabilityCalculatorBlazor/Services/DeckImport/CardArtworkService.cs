using System.Collections.Concurrent;
using System.Globalization;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Services.DeckImport;

/// <summary>Shared nonessential display lookups. Never exposes provider image URLs.</summary>
public sealed class CardArtworkService(ICardInfoService cardInfoService) : ICardArtworkService {
    public const string ArtworkOrigin = "https://ygo-calculator-artwork.ygo-probability.workers.dev";
    private readonly ConcurrentDictionary<int, Lazy<Task<string?>>> lookups = new();

    public Task<string?> GetArtworkUrlAsync(int externalCardId) => externalCardId is > 0 and <= 2147483647
        ? lookups.GetOrAdd(externalCardId, id => new(() => ResolveAsync(id))).Value
        : Task.FromResult<string?>(null);

    private async Task<string?> ResolveAsync(int id) {
        try {
            var info = await cardInfoService.GetCardArtworkInfoAsync(id);
            return info?.SelectArtworkImageId(id) is > 0 and <= 2147483647 and var imageId
                ? $"{ArtworkOrigin}/small/{imageId.ToString(CultureInfo.InvariantCulture)}.jpg" : null;
        }
        catch { return null; }
    }
}
