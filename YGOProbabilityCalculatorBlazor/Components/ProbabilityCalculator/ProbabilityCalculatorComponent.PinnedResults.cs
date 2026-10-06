using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

public partial class ProbabilityCalculatorComponent
{
    private readonly Dictionary<Combo, Guid> comparisonCombos = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, Guid> comparisonGroups = new(StringComparer.Ordinal);
    private int comparisonEpoch;
    private PinnedResultSnapshot? acceptedComparison;
    private PinnedResultSnapshot? pinnedResult;

    private bool CanPin => ! disposed && ! isCalculating && ! calculationResultIsStale &&
                           calculationResult is not null && acceptedComparison is { IsValid: true };

    private bool CanCompareDisplayedResult =>
        ! disposed && calculationResult is not null && acceptedComparison is { IsValid: true };

    private string PinUnavailableReason => CanPin
        ? ""
        : isCalculating
            ? "Calculation is running."
            : calculationResultIsStale
                ? "Inputs changed. Calculate again to pin a current result."
                : "Calculate a valid result before pinning.";

    private void PinCurrentResult()
    {
        if (CanPin)
        {
            pinnedResult = acceptedComparison;
        }
    }

    private void ClearPinnedResult() => pinnedResult = null;

    private Guid ComboLineage(Combo combo)
    {
        if (! comparisonCombos.TryGetValue(combo, out Guid id))
        {
            comparisonCombos[combo] = id = Guid.NewGuid();
        }

        return id;
    }

    private Guid GroupLineage(string groupId)
    {
        if (! comparisonGroups.TryGetValue(groupId, out Guid id))
        {
            comparisonGroups[groupId] = id = Guid.NewGuid();
        }

        return id;
    }

    private void TransferComparisonLineage(Combo oldCombo, Combo newCombo)
    {
        Guid id = ComboLineage(oldCombo);
        comparisonCombos.Remove(oldCombo);
        comparisonCombos[newCombo] = id;
    }

    private void InvalidateComparison(bool replacement)
    {
        if (replacement)
        {
            comparisonEpoch++;
            acceptedComparison = null;
            comparisonCombos.Clear();
            comparisonGroups.Clear();
        }
        else
        {
            foreach (Combo removed in comparisonCombos.Keys.Where(c => ! combos.Contains(c)).ToArray())
            {
                comparisonCombos.Remove(removed);
            }

            foreach (string removed in comparisonGroups.Keys.Where(id => ! comboGroups.Any(g => g.Id == id)).ToArray())
            {
                comparisonGroups.Remove(removed);
            }
        }
    }

    private PinnedCalculationContext CaptureComparisonContext() => PinnedCalculationContext.Capture(
        comparisonEpoch,
        handSize,
        cards,
        combos,
        comboGroups,
        ComboLineage,
        GroupLineage
    );

    private PinnedResultComparison? CurrentComboComparison(int index) => CanCompareDisplayedResult &&
                                                                         pinnedResult is not null &&
                                                                         acceptedComparison!.Combos.SingleOrDefault(c =>
                                                                             c.Index == index
                                                                         ) is { } row
        ? acceptedComparison.CompareComboPresentation(row, pinnedResult, true)
        : null;

    private PinnedResultComparison? CurrentGroupComparison(string id) => CanCompareDisplayedResult &&
                                                                         pinnedResult is not null &&
                                                                         acceptedComparison!.Groups.SingleOrDefault(g =>
                                                                             g.Id == id
                                                                         ) is { } row
        ? acceptedComparison.CompareGroupPresentation(row, pinnedResult, true)
        : null;
}
