using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Services.DeckImport;

public class DeckImportService(ICardInfoService cardInfoService) {
    public async Task<List<Card>> ImportDeckFromYdkAsync(string filePath) {
        var lines = await File.ReadAllLinesAsync(filePath);
        var cardCounts = new Dictionary<int, int>();
        var cards = new List<Card>();

        foreach (var raw in lines) {
            var line = raw.Trim();
            if (line.Equals("#extra", StringComparison.OrdinalIgnoreCase)) break;
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;

            if (!int.TryParse(line, out var cardId)) continue;
            if (!cardCounts.TryAdd(cardId, 1)) cardCounts[cardId]++;
        }

        foreach (var (id, count) in cardCounts) {
            try {
                var cardName = await cardInfoService.GetCardNameAsync(id);
                var card = new Card(new List<CategoryBase>(), count);
                cards.Add(card);
            }
            catch {
                var card = new Card(new List<CategoryBase>(), count);
                cards.Add(card);
            }
        }

        return cards;
    }
}
