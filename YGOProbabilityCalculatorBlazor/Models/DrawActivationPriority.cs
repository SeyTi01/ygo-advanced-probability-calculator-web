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

    // Retain inactive/effectless entries, but never transfer a removed identity's preference.
    public DrawActivationPriority RetainEntries(IEnumerable<Card> cards) {
        HashSet<string> existing = cards.Select(card => card.Id).ToHashSet(StringComparer.Ordinal);
        return CardIds.All(existing.Contains) ? this : new(Mode, CardIds.Where(existing.Contains).ToArray());
    }
}
