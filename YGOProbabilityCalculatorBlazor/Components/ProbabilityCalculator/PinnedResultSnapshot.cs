using YGOProbabilityCalculatorBlazor.Localization;
using System.Collections.Immutable;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

public sealed record PinnedCalculationContext(
    int HandSize,
    int Copies,
    int CardCount,
    CalculationModel Model = CalculationModel.Fast,
    bool UsesCustomDrawPriority = false,
    FastExecutionStrategy Strategy = FastExecutionStrategy.Progressive
) {
    public string ModeDescription => (Model, Strategy) switch {
        (CalculationModel.Fast, FastExecutionStrategy.FastOnly) => UiText.Get("Ui.FastOnly"),
        (CalculationModel.Fast, _) => UiText.Get("Ui.Progressive"),
        (CalculationModel.Exact, _) => UiText.Get("Ui.ExactOnly"),
        (CalculationModel.OpeningHand, _) => UiText.Get("Ui.NoEffectSimulation"),
        _ => throw new ArgumentOutOfRangeException(nameof(Model))
    };

    public string Description => UiText.Format("Ui.HandSize01Cards", HandSize, Copies);

    internal static PinnedCalculationContext Capture(
        int handSize,
        IReadOnlyList<Card> cards,
        CalculationModel model,
        DrawActivationPriority? priority
    ) {
        Card[] activeCards = [.. cards.Where(card => card.Active)];
        bool usesCustomDrawPriority = model != CalculationModel.OpeningHand
            && priority?.Mode == DrawActivationMode.CustomPriority;

        return new(handSize, activeCards.Sum(card => card.Copies), activeCards.Length, model, usesCustomDrawPriority);
    }
}

public sealed record PinnedComboRow(int Index, string Label, double Probability, string? GroupId);

public sealed record PinnedGroupRow(string Id, string Label, double Probability, int ActiveCount);

public sealed record PinnedResultSnapshot(
    PinnedCalculationContext Context,
    double Total,
    ImmutableArray<PinnedComboRow> Combos,
    ImmutableArray<PinnedGroupRow> Groups
) {
    public SearchAvailabilityCertificate? SearchAvailability { get; init; }
    public CalculationResultContract? Contract { get; init; }

    public static PinnedResultSnapshot Capture(PinnedCalculationContext context, ProbabilityCalculationResult result) {
        ImmutableArray<PinnedComboRow> pinnedComboRows = result.ComboProbabilities
            .Select(combo => new PinnedComboRow(
                combo.ComboIndex,
                string.IsNullOrWhiteSpace(combo.ComboName) ? UiText.Format("Ui.UnnamedCombo0", combo.ComboIndex + 1) : combo.ComboName,
                combo.Probability,
                combo.GroupId))
            .ToImmutableArray();

        ImmutableArray<PinnedGroupRow> pinnedGroupRows = (result.GroupProbabilities ?? [])
            .Select(group => new PinnedGroupRow(group.GroupId, group.GroupName, group.Probability, group.ActiveComboCount))
            .ToImmutableArray();

        return new(context, result.TotalProbability, pinnedComboRows, pinnedGroupRows) {
            SearchAvailability = result.SearchAvailability,
            Contract = result.Contract
        };
    }

    public bool IsValid => Valid(Total) && Combos.All(combo => Valid(combo.Probability)) && Groups.All(group => Valid(group.Probability));

    private static bool Valid(double value) => double.IsFinite(value) && value is >= 0 and <= 1;
}
