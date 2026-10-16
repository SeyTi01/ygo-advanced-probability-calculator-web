using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

public partial class ProbabilityCalculatorComponent {
    private PinnedResultSnapshot? _acceptedResultSnapshot;
    private PinnedResultSnapshot? _pinnedResult;

    private bool CanPin {
        get {
            if (_disposed || _isCalculating || _calculationResultIsStale || _calculationResult is null) {
                return false;
            }

            return _acceptedResultSnapshot is { IsValid: true };
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
            _pinnedResult = _acceptedResultSnapshot;
        }
    }

    private void ClearPinnedResult() {
        _pinnedResult = null;
    }

    private PinnedCalculationContext CaptureResultContext(CalculationModel model) => PinnedCalculationContext.Capture(
        _handSize,
        _cards,
        model,
        _drawActivationPriority
    );
}
