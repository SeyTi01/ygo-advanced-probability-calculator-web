using Microsoft.AspNetCore.Components.Forms;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Services.DeckImport;

public class DeckImportService(ICardInfoService cardInfoService, IFileService fileService) : IDeckImportService {
    public async Task<List<Card>> ImportDeckFromYdkAsync(IBrowserFile file) {
        var lines = await fileService.ReadAllLinesAsync(file);
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
            CardInfo info;
            try {
                info = await cardInfoService.GetCardInfoAsync(id);
            }
            catch {
                info = new CardInfo { Id = id, Name = id.ToString(System.Globalization.CultureInfo.InvariantCulture) };
            }

            var card = new Card(CardPropertyProvider.GetCategories(info), count, info.Name, externalCardId: id);
            cards.Add(card);
        }

        return cards;
    }
}
