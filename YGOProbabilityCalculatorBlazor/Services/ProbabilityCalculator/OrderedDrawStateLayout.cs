using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

// One descriptor per ordered request, never one per state. Offsets describe the
// original int[] encoding; optional regions occupy no cells when absent. The
// exchangeable resolver deliberately keeps its independent three-class layout.
internal readonly struct OrderedDrawStateLayout {
    private readonly int[] _optSlots;

    internal int RowCount { get; }
    internal int HandStart => RowCount;
    internal int DrawsLeft { get; }
    internal int CompletionSource => DrawsLeft + 1;
    internal int HiddenCount { get; }
    // 0 = no activation, 1 = activated, 2 = draws locked. Part of state identity.
    internal int ActivationState { get; }
    internal int QueueStart { get; }
    internal bool HasHiddenCount => HiddenCount >= 0;
    internal bool HasActivationState => ActivationState >= 0;

    internal OrderedDrawStateLayout(IReadOnlyList<Card> cards) {
        RowCount = cards.Count;
        _optSlots = new int[RowCount];
        Array.Fill(_optSlots, -1);
        int optCount = 0;
        for (int i = 0; i < RowCount; i++) {
            if (cards[i].DrawCount is not null && cards[i].DrawOncePerTurn) {
                _optSlots[i] = 2 * RowCount + optCount++;
            }
        }
        DrawsLeft = 2 * RowCount + optCount;
        bool hasHidden = cards.Any(card => card.PreDrawDeckRemovalCount > 0 || card.PostDrawDeckMillCount > 0);
        bool hasRestrictions = cards.Any(card => card.EffectPlan.HasActivationRestrictions);
        HiddenCount = hasHidden ? CompletionSource + 1 : -1;
        ActivationState = hasRestrictions ? CompletionSource + 1 + (hasHidden ? 1 : 0) : -1;
        QueueStart = CompletionSource + 1 + (hasHidden ? 1 : 0) + (hasRestrictions ? 1 : 0);
    }

    internal int OptSlot(int entry) => _optSlots[entry];
}
