namespace CardProbability.Internal.Model;

internal enum DrawActivationMode { EncounterOrder, CustomPriority }

// One owned list supplies independent relative draw/search activation orders.
internal sealed class DrawActivationPriority {
    public static DrawActivationPriority Default { get; } = new();
    public DrawActivationMode Mode { get; }
    public IReadOnlyList<string> CardIds { get; }
    private readonly Dictionary<string, int> _ranks;

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
                throw new ArgumentException("Draw activation priority requires nonempty, unique entry IDs.", nameof(cardIds));
            }
        }
        Mode = mode;
        CardIds = Array.AsReadOnly(owned);
    }

    public int Rank(string cardId) => Mode == DrawActivationMode.CustomPriority
        ? _ranks.GetValueOrDefault(cardId, int.MaxValue) : int.MaxValue;

}
