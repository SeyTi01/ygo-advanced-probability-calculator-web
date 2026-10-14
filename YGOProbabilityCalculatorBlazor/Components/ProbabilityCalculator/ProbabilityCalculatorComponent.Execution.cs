using Microsoft.AspNetCore.Components;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

public partial class ProbabilityCalculatorComponent {
    [Parameter]
    public TimeProvider CalculationTimeProvider { get; set; } = TimeProvider.System;

    private Task _workerCompletion = Task.CompletedTask;
    private Task _recoveryCleanup = Task.CompletedTask;
    private CalculationSettingsMenu? _settingsMenu;
    private bool _openingHandAlternativeAvailable;
    private ProbabilityCalculationLimitReason? _limitReason;
    private bool _assessmentDirty = true;
    private DrawExecutionAssessment _executionAssessment;
    private int[] _performanceCandidateIndices = [];
    private CalculationModel SelectedModel => CalculationModelPresentation.FromDrawEffects(_drawEffectsEnabled);

    private DrawExecutionAssessment ExecutionAssessment {
        get {
            if (_assessmentDirty) {
                IReadOnlyList<Card> cards = CategoryMembership.Materialize(_cards, _categoryBases);
                _executionAssessment = DrawExecutionAssessment.Assess(cards, _combos, _handSize, _drawEffectsEnabled);
                _performanceCandidateIndices = _executionAssessment.ElevatedRisk
                    ? Enumerable.Range(0, cards.Count).Where(index => cards[index].Active && cards[index].Copies > 0
                        && DrawEffectPerformance.RequiresDetailedHandState(cards[index].EffectPlan, cards[index].EffectProfile.ExecutionModel)).Take(3).ToArray()
                    : [];
                _assessmentDirty = false;
            }

            return _executionAssessment;
        }
    }

    // Keep attribution bounded and names current without reassessing on name edits.
    private string PerformanceCandidates => string.Join(", ", _performanceCandidateIndices.Take(2)
        .Select(index => string.IsNullOrWhiteSpace(_cards[index].Name) ? $"Card {index + 1}" : _cards[index].Name))
        + (_performanceCandidateIndices.Length > 2 ? " and others" : "");

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
