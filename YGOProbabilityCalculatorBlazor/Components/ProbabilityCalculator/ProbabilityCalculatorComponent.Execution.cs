using Microsoft.AspNetCore.Components;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

public partial class ProbabilityCalculatorComponent {
    [Parameter]
    public TimeProvider CalculationTimeProvider { get; set; } = TimeProvider.System;

    private Task _workerCompletion = Task.CompletedTask;
    private Task _recoveryCleanup = Task.CompletedTask;
    private CalculationSettingsMenu? _settingsMenu;
    private bool _stillCalculating;
    private bool _openingHandAlternativeAvailable;
    private CalculationModel _runningModel;
    private ProbabilityCalculationLimitReason? _limitReason;
    private bool _assessmentDirty = true;
    private DrawExecutionAssessment _executionAssessment;
    private CalculationModel SelectedModel => CalculationModelPresentation.FromFlags(_drawEffectsEnabled, _conditionalEffectsEnabled);

    private DrawExecutionAssessment ExecutionAssessment {
        get {
            if (_assessmentDirty) {
                _executionAssessment = DrawExecutionAssessment.Assess(_cards, _combos, _handSize, _drawEffectsEnabled, _conditionalEffectsEnabled);
                _assessmentDirty = false;
            }

            return _executionAssessment;
        }
    }

    private async Task ShowElapsedNotices(Func<bool> ownsRequest, CancellationToken cancellationToken) {
        try {
            Task slowNotice = Task.Delay(TimeSpan.FromSeconds(1), CalculationTimeProvider, cancellationToken);
            Task alternativeNotice = Task.Delay(TimeSpan.FromSeconds(3), CalculationTimeProvider, cancellationToken);
            await slowNotice;
            await InvokeAsync(() => {
                if (ownsRequest() && _isCalculating) {
                    _stillCalculating = true;
                    StateHasChanged();
                }
            });
            await alternativeNotice;
            await InvokeAsync(() => {
                if (ownsRequest() && _isCalculating && _runningModel != CalculationModel.OpeningHand) {
                    _openingHandAlternativeAvailable = true;
                    StateHasChanged();
                }
            });
        }
        catch (OperationCanceledException) {
            // Delayed notices belong only to this request.
        }
    }

    private Task CalculateOpeningHandOnly() {
        if (_disposed || !CanCalculate) {
            return Task.CompletedTask;
        }

        _recoveryCleanup = _workerCompletion;
        StopCalculation();
        return CalculateModel(CalculationModel.OpeningHand);
    }

    private void DismissCalculationError() {
        _errorMessage = null;
        _limitReason = null;
    }

    private Task ReviewCalculationSettings() => _settingsMenu?.Open() ?? Task.CompletedTask;
}
