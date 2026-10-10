using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.Interface;

public interface ICardInfoService {
    Task<IReadOnlyList<CardInfo>> SearchCardsAsync(string query);

    Task<string> GetCardNameAsync(int id);

    Task<CardInfo> GetCardInfoAsync(int id);

    Task<CardInfo> GetCardArtworkInfoAsync(int id) => GetCardInfoAsync(id);

    Task<IReadOnlyDictionary<string, CardInfo>> GetCardInfoByExactNamesAsync(IEnumerable<string> names);
}
