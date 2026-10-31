using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.DeckImport;

// Built once from the validated local catalog; shared by names and metadata lookups.
internal sealed class CardCatalogIndex {
    public IReadOnlyDictionary<int, CardInfo> CanonicalCards { get; }
    private readonly Dictionary<int, CardInfo?> _artworkCards = new();

    public CardCatalogIndex(IReadOnlyList<CardInfo> cards) {
        CanonicalCards = cards.ToDictionary(card => card.Id);
        foreach (CardInfo card in cards) {
            foreach (int id in card.ArtworkImageIds) {
                // A canonical passcode always takes precedence over an artwork alias.
                if (CanonicalCards.ContainsKey(id)) {
                    continue;
                }

                if (_artworkCards.TryGetValue(id, out CardInfo? previous)) {
                    if (!ReferenceEquals(previous, card)) { _artworkCards[id] = null; }
                }
                else {
                    _artworkCards.Add(id, card);
                }
            }
        }
    }

    public CardInfo? Find(int id) => CanonicalCards.TryGetValue(id, out CardInfo? card)
        ? card
        : _artworkCards.GetValueOrDefault(id);
}
