using Microsoft.AspNetCore.Components;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

public partial class ProbabilityCalculatorComponent {
    private PinnedResultSnapshot? _acceptedResultSnapshot;
    private PinnedResultSnapshot? _pinnedResult;
    private PinnedResultSnapshot? _exactRetryCandidate;
    private long _exactRetryWorkspaceVersion;
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

    private Task RequestExactRetry() {
        if (!CanRetryExact) {
            return Task.CompletedTask;
        }

        _exactRetryCandidate = _acceptedResultSnapshot;
        _exactRetryWorkspaceVersion = _workspaceEditVersion;
        if (_pinnedResult is not null && !ReferenceEquals(_pinnedResult, _exactRetryCandidate)) {
            return Task.CompletedTask;
        }

        return ConfirmExactRetry();
    }

    private async Task ConfirmExactRetry() {
        PinnedResultSnapshot? candidate = _exactRetryCandidate;
        bool valid = CanRetryExact && candidate is not null
            && ReferenceEquals(candidate, _acceptedResultSnapshot)
            && _exactRetryWorkspaceVersion == _workspaceEditVersion;
        CancelExactRetry();
        if (!valid) {
            return;
        }

        // No await between validating the accepted snapshot, pinning and owning the request.
        _pinnedResult = candidate;
        Task calculation = CalculateModel(CalculationModel.Exact);
        await _calculationAction.FocusAsync(preventScroll: true);
        await calculation;
    }

    private void CancelExactRetry() => _exactRetryCandidate = null;

    private async Task DismissExactRetry() {
        CancelExactRetry();
        await _calculationAction.FocusAsync(preventScroll: true);
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
        CancelExactRetry();
        if (CanPin) {
            _pinnedResult = _acceptedResultSnapshot;
        }
    }

    private void ClearPinnedResult() {
        CancelExactRetry();
        _pinnedResult = null;
    }

    private PinnedCalculationContext CaptureResultContext(CalculationModel model) => PinnedCalculationContext.Capture(
        _handSize,
        _cards,
        model,
        _drawActivationPriority
    );
}
