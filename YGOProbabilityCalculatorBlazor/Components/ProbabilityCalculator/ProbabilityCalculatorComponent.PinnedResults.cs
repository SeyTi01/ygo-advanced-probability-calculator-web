using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

public partial class ProbabilityCalculatorComponent {
    private readonly Dictionary<Combo, Guid> _comparisonCombos = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, Guid> _comparisonGroups = new(StringComparer.Ordinal);
    private int _comparisonEpoch;
    private PinnedResultSnapshot? _acceptedComparison;
    private PinnedResultSnapshot? _pinnedResult;

    private bool CanPin {
        get {
            if (_disposed || _isCalculating || _calculationResultIsStale || _calculationResult is null) {
                return false;
            }

            return _acceptedComparison is { IsValid: true };
        }
    }

    private bool CanCompareDisplayedResult {
        get {
            if (_disposed || _calculationResult is null) {
                return false;
            }

            return _acceptedComparison is { IsValid: true };
        }
    }

    private string PinUnavailableReason {
        get {
            if (CanPin) {
                return "";
            }

            if (_isCalculating) {
                return "Calculation is running.";
            }

            if (_calculationResultIsStale) {
                return "Inputs changed. Calculate again to pin a current result.";
            }

            return "Calculate a valid result before pinning.";
        }
    }

    private void PinCurrentResult() {
        if (CanPin) {
            _pinnedResult = _acceptedComparison;
        }
    }

    private void ClearPinnedResult() {
        _pinnedResult = null;
    }

    private Guid ComboLineage(Combo combo) {
        if (!_comparisonCombos.TryGetValue(combo, out Guid id)) {
            id = Guid.NewGuid();
            _comparisonCombos[combo] = id;
        }

        return id;
    }

    private Guid GroupLineage(string groupId) {
        if (!_comparisonGroups.TryGetValue(groupId, out Guid id)) {
            id = Guid.NewGuid();
            _comparisonGroups[groupId] = id;
        }

        return id;
    }

    private void TransferComparisonLineage(Combo oldCombo, Combo newCombo) {
        Guid id = ComboLineage(oldCombo);
        _comparisonCombos.Remove(oldCombo);
        _comparisonCombos[newCombo] = id;
    }

    private void InvalidateComparison(bool replacement) {
        if (replacement) {
            _comparisonEpoch++;
            _acceptedComparison = null;
            _comparisonCombos.Clear();
            _comparisonGroups.Clear();
        }
        else {
            foreach (Combo removedCombo in _comparisonCombos.Keys.Where(combo => !_combos.Contains(combo)).ToArray()) {
                _comparisonCombos.Remove(removedCombo);
            }

            foreach (string removedGroupId in _comparisonGroups.Keys
                         .Where(groupId => _comboGroups.All(group => group.Id != groupId))
                         .ToArray()) {

                _comparisonGroups.Remove(removedGroupId);
            }
        }
    }

    private PinnedCalculationContext CaptureComparisonContext() => PinnedCalculationContext.Capture(
        _comparisonEpoch,
        _handSize,
        _cards,
        _combos,
        _comboGroups,
        ComboLineage,
        GroupLineage
    );

    private PinnedResultComparison? CurrentComboComparison(int index) {
        if (!CanCompareDisplayedResult || _pinnedResult is null) {
            return null;
        }

        PinnedResultSnapshot accepted = _acceptedComparison!;
        PinnedComboRow? row = accepted.Combos.SingleOrDefault(combo => combo.Index == index);

        if (row is null) {
            return null;
        }

        return accepted.CompareComboPresentation(row, _pinnedResult, true);
    }

    private PinnedResultComparison? CurrentGroupComparison(string id) {
        if (!CanCompareDisplayedResult || _pinnedResult is null) {
            return null;
        }

        PinnedResultSnapshot accepted = _acceptedComparison!;
        PinnedGroupRow? row = accepted.Groups.SingleOrDefault(group => group.Id == id);

        if (row is null) {
            return null;
        }

        return accepted.CompareGroupPresentation(row, _pinnedResult, true);
    }
}
