using Microsoft.AspNetCore.Components;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

public partial class ProbabilityCalculatorComponent {
    private PinnedResultSnapshot? _acceptedResultSnapshot;
    private PinnedResultSnapshot? _pinnedResult;
    private ElementReference _calculationAction;

    private bool CanRetryExact => CanPin && CanCalculate && !_calculationResultIsStale
        && _acceptedResultSnapshot is {
            Context.Model: CalculationModel.Fast,
            Contract: {
                RequestedMode: CalculationModel.Fast,
                Completion: CalculationCompletion.Success,
                Accuracy: CalculationAccuracy.Bounded or CalculationAccuracy.Uncertified
            }
        };

    private async Task RequestExactRetry() {
        if (!CanRetryExact) {
            return;
        }

        Task calculation = CalculateModel(CalculationModel.Exact);
        await _calculationAction.FocusAsync(preventScroll: true);
        await calculation;
    }

    private bool CanPin {
        get {
            if (_disposed || _isCalculating || _calculationResult is null) {
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
