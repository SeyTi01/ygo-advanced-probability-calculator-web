using Microsoft.AspNetCore.Components.Forms;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Services.DeckImport;

public class DeckImportService(ICardInfoService cardInfoService, IFileService fileService) : IDeckImportService
{
    public async Task<List<Card>> ImportDeckFromYdkAsync(IBrowserFile file)
    {
        string[] lines = await fileService.ReadAllLinesAsync(file);
        List<int> cardIds = [];

        foreach (string raw in lines)
        {
            string line = raw.Trim();

            if (line.Equals("#extra", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                continue;
            }

            if (int.TryParse(line, out int cardId))
            {
                cardIds.Add(cardId);
            }
        }

        return await ImportMainDeckCardIdsAsync(cardIds);
    }

    public Task<List<Card>> ImportDeckFromYdkeAsync(string ydke)
    {
        YdkeDeck deck = YdkeParser.Parse(ydke);
        List<int> mainCardIds = new(deck.MainDeck.Count);

        foreach (uint cardId in deck.MainDeck)
        {
            if (cardId > int.MaxValue)
            {
                throw new FormatException($"Card passcode {cardId} is outside the supported card ID range.");
            }

            mainCardIds.Add((int)cardId);
        }

        return ImportMainDeckCardIdsAsync(mainCardIds);
    }

    private async Task<List<Card>> ImportMainDeckCardIdsAsync(IEnumerable<int> cardIds)
    {
        Dictionary<int, int> cardCounts = [];
        List<int> orderedCardIds = [];

        foreach (int cardId in cardIds)
        {
            if (cardCounts.TryGetValue(cardId, out int count))
            {
                cardCounts[cardId] = count + 1;
            }
            else
            {
                cardCounts.Add(cardId, 1);
                orderedCardIds.Add(cardId);
            }
        }

        List<Card> cards = new(orderedCardIds.Count);

        foreach (int id in orderedCardIds)
        {
            CardInfo info;

            try
            {
                info = await cardInfoService.GetCardInfoAsync(id);
            }
            catch
            {
                info = new CardInfo
                {
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
