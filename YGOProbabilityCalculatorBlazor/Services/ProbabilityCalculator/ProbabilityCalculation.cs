using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

// One instance per service call: compilation storage, count caches and the shared
// work budget remain local to this request, including total/combo/group unions.
internal sealed class ProbabilityCalculation {
    private readonly ComboEventCompiler _compiler;
    private readonly EventUnionEvaluator _unionEvaluator;

    internal ProbabilityCalculation(List<Card> deck, int handSize, CalculationWorkPolicy workPolicy) {
        WorkBudget budget = new(workPolicy);
        _compiler = new(deck, handSize, budget);
        ExactHandCounter counter = new(deck, handSize, budget);
        _unionEvaluator = new(deck.Count, handSize, budget, _compiler, counter);
    }

    internal double CalculateUnion(List<Combo> combos) {
        List<CompiledEvent?> events = combos.SelectMany(combo => _compiler.CompileCombo(combo)).ToList();

        return _unionEvaluator.Union(events);
    }

    internal ProbabilityCalculationResult CalculateResults(List<Combo> combos, IReadOnlyList<ComboGroup>? groups) {
        List<List<CompiledEvent?>> ownedEvents = combos.Select(combo => _compiler.CompileCombo(combo)).ToList();
        List<CompiledEvent?> events = ownedEvents.SelectMany(static comboEvents => comboEvents).ToList();
        double totalProbability = _unionEvaluator.Union(events);

        List<ComboProbabilityResult> comboProbabilities = combos
            .Select((combo, index) => new ComboProbabilityResult(index, combo.Name, _unionEvaluator.Union(ownedEvents[index]), combo.GroupId))
            .ToList();

        List<GroupProbabilityResult> groupProbabilities = (groups ?? [])
            .Select(group => {
                List<List<CompiledEvent?>> members = ownedEvents.Where((_, index) => combos[index].GroupId == group.Id).ToList();

                return new GroupProbabilityResult(
                    group.Id,
                    group.Name,
                    _unionEvaluator.Union(members.SelectMany(static comboEvents => comboEvents).ToList()),
                    members.Count
                );
            })
            .ToList();

        return new ProbabilityCalculationResult(totalProbability, comboProbabilities.AsReadOnly(), groupProbabilities.AsReadOnly());
    }
}
