using YGOProbabilityCalculatorBlazor.Localization;
using Microsoft.AspNetCore.Components;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

public partial class ProbabilityCalculatorComponent {
    private PinnedResultSnapshot? _pinnedResult;
    private ElementReference _calculationAction;

    private bool CanRetryExact => CanPin && CanCalculate && !_calculation.IsStale
        && _calculation.Snapshot is {
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
            if (_disposed || _calculation.IsCalculating || _calculation.Result is null) {
                return false;
            }

            return _calculation.Snapshot is { IsValid: true };
        }
    }

    private string PinUnavailableReason {
        get {
            if (CanPin) {
                return "";
            }

            if (_calculation.IsCalculating) {
                return UiText.Get("Ui.CalculationIsRunning");
            }

            return UiText.Get("Ui.CalculateAValidResultBeforePinning");
        }
    }

    private void PinCurrentResult() {
        if (CanPin) {
            _pinnedResult = _calculation.Snapshot;
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
    ) with { Strategy = model == CalculationModel.Fast ? _fastStrategy : FastExecutionStrategy.Progressive };
}
