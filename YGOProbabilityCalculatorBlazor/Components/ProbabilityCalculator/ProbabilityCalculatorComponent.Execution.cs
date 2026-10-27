using YGOProbabilityCalculatorBlazor.Localization;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

public partial class ProbabilityCalculatorComponent {
    private bool _assessmentDirty = true;
    private DrawExecutionAssessment _executionAssessment;
    private int[] _performanceCandidateIndices = [];
    private CalculationModel SelectedModel => _selectedModel;

    private DrawExecutionAssessment ExecutionAssessment {
        get {
            if (_assessmentDirty) {
                IReadOnlyList<Card> cards = CategoryMembership.Materialize(_cards, _categoryBases);
                _executionAssessment = DrawExecutionAssessment.Assess(cards, _combos, _handSize, SelectedModel != CalculationModel.OpeningHand);
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
        .Select(index => string.IsNullOrWhiteSpace(_cards[index].Name) ? UiText.Format("Ui.Card0", index + 1) : _cards[index].Name))
        + (_performanceCandidateIndices.Length > 2 ? UiText.Get("Ui.AndOthers") : "");

    private void DismissCalculationError() {
        _errorMessage = null;
    }
}
