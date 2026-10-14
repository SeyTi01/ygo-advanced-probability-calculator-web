using System.Text.Json.Serialization;

namespace YGOProbabilityCalculatorBlazor.Models;

public enum DrawActivationMode { EncounterOrder, CustomPriority }

// Own the ordered IDs so UI edits and deserialization cannot mutate an in-flight policy.
public sealed class DrawActivationPriority {
    public static DrawActivationPriority Default { get; } = new();
    public DrawActivationMode Mode { get; }
    public IReadOnlyList<string> CardIds { get; }
    private readonly Dictionary<string, int> _ranks;

    [JsonConstructor]
    public DrawActivationPriority(
        DrawActivationMode mode = DrawActivationMode.EncounterOrder,
        IReadOnlyList<string>? cardIds = null
    ) {
        if (!Enum.IsDefined(mode)) {
            throw new ArgumentOutOfRangeException(nameof(mode), "Unknown draw activation priority mode.");
        }
        string[] owned = cardIds?.ToArray() ?? [];
        _ranks = new(StringComparer.Ordinal);
        for (int i = 0; i < owned.Length; i++) {
            if (string.IsNullOrWhiteSpace(owned[i]) || !_ranks.TryAdd(owned[i], i)) {
                throw new ArgumentException("Draw activation priority requires nonempty, unique workspace card IDs.", nameof(cardIds));
            }
        }
        Mode = mode;
        CardIds = Array.AsReadOnly(owned);
    }

    public int Rank(string cardId) => Mode == DrawActivationMode.CustomPriority
        ? _ranks.GetValueOrDefault(cardId, int.MaxValue) : int.MaxValue;

    // Preserve existing preferences and append new effect identities in a stable order.
    // Visual deck sorting must not change the custom activation order.
    public DrawActivationPriority ForWorkspace(IEnumerable<Card> cards) {
        Card[] entries = cards.ToArray();
        HashSet<string> existing = entries.Select(card => card.Id).ToHashSet(StringComparer.Ordinal);
        List<string> ids = CardIds.Where(existing.Contains).ToList();
        if (Mode == DrawActivationMode.CustomPriority) {
            HashSet<string> ranked = ids.ToHashSet(StringComparer.Ordinal);
            ids.AddRange(entries.Where(card => card.DrawCount is not null && ranked.Add(card.Id))
                .Select(card => card.Id).Order(StringComparer.Ordinal));
        }

        return CardIds.SequenceEqual(ids) ? this : new(Mode, ids);
    }
}
