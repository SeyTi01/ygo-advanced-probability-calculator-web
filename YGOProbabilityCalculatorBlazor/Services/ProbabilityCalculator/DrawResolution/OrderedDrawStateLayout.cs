using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.DrawResolution;

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
    internal int SearchQueueStart { get; }
    internal int SearchQueueCapacity { get; }
    internal bool HasSearchQueue => SearchQueueCapacity > 0;
    internal bool HasHiddenCount => HiddenCount >= 0;
    internal bool HasActivationState => ActivationState >= 0;

    internal OrderedDrawStateLayout(IReadOnlyList<Card> cards, bool trackSearchOrder = false) {
        RowCount = cards.Count;
        _optSlots = new int[RowCount];
        Array.Fill(_optSlots, -1);
        int optCount = 0;
        for (int i = 0; i < RowCount; i++) {
            if (cards[i].DrawCount is not null && cards[i].MaximumActivations != int.MaxValue) {
                _optSlots[i] = 2 * RowCount + optCount++;
            }
        }
        DrawsLeft = 2 * RowCount + optCount;
        bool hasHidden = cards.Any(card => card.PreDrawDeckRemovalCount > 0 || card.PostDrawDeckMillCount > 0);
        bool hasRestrictions = cards.Any(card => card.EffectPlan.HasActivationRestrictions);
        HiddenCount = hasHidden ? CompletionSource + 1 : -1;
        ActivationState = hasRestrictions ? CompletionSource + 1 + (hasHidden ? 1 : 0) : -1;
        SearchQueueStart = CompletionSource + 1 + (hasHidden ? 1 : 0) + (hasRestrictions ? 1 : 0);
        SearchQueueCapacity = trackSearchOrder ? cards.Where(card => card.Search is not null).Sum(card => card.Copies) : 0;
        QueueStart = SearchQueueStart + SearchQueueCapacity;
    }

    internal void EncounterSearch(int[] state, int row) {
        for (int index = SearchQueueStart; index < QueueStart; index++) {
            if (state[index] == 0) {
                state[index] = row + 1;
                return;
            }
        }
    }

    internal void RemoveSearch(int[] state, int row) {
        for (int index = QueueStart - 1; index >= SearchQueueStart; index--) {
            if (state[index] == row + 1) {
                Array.Copy(state, index + 1, state, index, QueueStart - index - 1);
                state[QueueStart - 1] = 0;
                return;
            }
        }
    }

    internal int OptSlot(int entry) => _optSlots[entry];
}
