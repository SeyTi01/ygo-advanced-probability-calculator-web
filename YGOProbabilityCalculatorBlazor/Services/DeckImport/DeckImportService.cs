using Microsoft.AspNetCore.Components.Forms;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Services.DeckImport;

public class DeckImportService(ICardInfoService cardInfoService, IFileService fileService) : IDeckImportService {
    public async Task<List<Card>> ImportDeckFromYdkAsync(IBrowserFile file) {
        var lines = await fileService.ReadAllLinesAsync(file);
        var cardIds = new List<int>();

        foreach (var raw in lines) {
            var line = raw.Trim();
            if (line.Equals("#extra", StringComparison.OrdinalIgnoreCase)) break;
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;

            if (int.TryParse(line, out var cardId))
                cardIds.Add(cardId);
        }

        return await ImportMainDeckCardIdsAsync(cardIds);
    }

    public Task<List<Card>> ImportDeckFromYdkeAsync(string ydke) {
        var deck = YdkeParser.Parse(ydke);
        var mainCardIds = new List<int>(deck.MainDeck.Count);
        foreach (var cardId in deck.MainDeck) {
            if (cardId > int.MaxValue)
                throw new FormatException($"Card passcode {cardId} is outside the supported card ID range.");

            mainCardIds.Add((int)cardId);
        }

        return ImportMainDeckCardIdsAsync(mainCardIds);
    }

    private async Task<List<Card>> ImportMainDeckCardIdsAsync(IEnumerable<int> cardIds) {
        var cardCounts = new Dictionary<int, int>();
        var orderedCardIds = new List<int>();

        foreach (var cardId in cardIds) {
            if (cardCounts.TryGetValue(cardId, out var count)) {
                cardCounts[cardId] = count + 1;
            }
            else {
                cardCounts.Add(cardId, 1);
                orderedCardIds.Add(cardId);
            }
        }

        var cards = new List<Card>(orderedCardIds.Count);
        foreach (var id in orderedCardIds) {
            CardInfo info;
            try {
                info = await cardInfoService.GetCardInfoAsync(id);
            }
            catch {
                info = new CardInfo {
                    Id = id,
                    Name = id.ToString(System.Globalization.CultureInfo.InvariantCulture)
                };
            }

            cards.Add(new Card(
                CardPropertyProvider.GetCategories(info),
                cardCounts[id],
                info.Name,
                externalCardId: id));
        }

        return cards;
    }
}
