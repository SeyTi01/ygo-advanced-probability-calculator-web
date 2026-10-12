using Microsoft.AspNetCore.Components;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

public partial class ProbabilityCalculatorComponent {
    [Parameter]
    public TimeProvider CalculationTimeProvider { get; set; } = TimeProvider.System;

    private Task _workerCompletion = Task.CompletedTask;
    private Task _recoveryCleanup = Task.CompletedTask;
    private CalculationSettingsMenu? _settingsMenu;
    private bool _openingHandAlternativeAvailable;
    private ProbabilityCalculationLimitReason? _limitReason;
    private CalculationModel SelectedModel => CalculationModelPresentation.FromFlags(_drawEffectsEnabled, _conditionalEffectsEnabled);

    private async Task ShowRecoveryAlternative(Func<bool> ownsRequest, CalculationModel model, CancellationToken cancellationToken) {
        try {
            await Task.Delay(TimeSpan.FromSeconds(3), CalculationTimeProvider, cancellationToken);
            await InvokeAsync(() => {
                if (ownsRequest() && _isCalculating && model != CalculationModel.OpeningHand) {
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
