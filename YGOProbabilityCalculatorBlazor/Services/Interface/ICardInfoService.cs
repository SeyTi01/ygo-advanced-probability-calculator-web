using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.Interface;

public interface ICardInfoService {
    // Same-origin verified catalog only; never cache/API enrichment during reconciliation.
    Task<IReadOnlyList<CardInfo>> GetCatalogCardsAsync() => Task.FromResult<IReadOnlyList<CardInfo>>([]);

    Task<IReadOnlyList<CardInfo>> SearchCardsAsync(string query);

    // Optional presentation only; this accessor must not fetch data or change CardInfo.Name.
    string? GetLocalizedCardName(CardInfo info) => null;

    // Saved-card display remains separate from stored names and requires verified catalog identity.
    string? GetLocalizedCardName(Card card) => null;
    Task<string?> GetLocalizedCardNameAsync(Card card) => Task.FromResult<string?>(null);

    Task<string> GetCardNameAsync(int id);

    Task<CardInfo> GetCardInfoAsync(int id);

    Task<CardInfo> GetCardArtworkInfoAsync(int id) => GetCardInfoAsync(id);

    Task<IReadOnlyDictionary<string, CardInfo>> GetCardInfoByExactNamesAsync(IEnumerable<string> names);
}
