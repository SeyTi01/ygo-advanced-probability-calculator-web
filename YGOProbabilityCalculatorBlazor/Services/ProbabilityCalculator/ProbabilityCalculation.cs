using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

// One instance per service call: compilation storage, count caches and the shared
// work budget remain local to this request, including total/combo/group unions.
internal sealed class ProbabilityCalculation {
    private readonly ComboEventCompiler compiler;
    private readonly EventUnionEvaluator unionEvaluator;

    internal ProbabilityCalculation(List<Card> deck, int handSize, CalculationWorkPolicy workPolicy) {
        var budget = new WorkBudget(workPolicy);
        compiler = new(deck, handSize, budget);
        var counter = new ExactHandCounter(deck, handSize, budget);
        unionEvaluator = new(deck.Count, handSize, budget, compiler, counter);
    }

    internal double CalculateUnion(List<Combo> combos) =>
        unionEvaluator.Union(combos.SelectMany(combo => compiler.CompileCombo(combo)).ToList());

    internal ProbabilityCalculationResult CalculateResults(List<Combo> combos, IReadOnlyList<ComboGroup>? groups) {
        var ownedEvents = combos.Select(combo => compiler.CompileCombo(combo)).ToList();
        var events = ownedEvents.SelectMany(e => e).ToList();
        var totalProbability = unionEvaluator.Union(events);
        var comboProbabilities = combos.Select((combo, index) =>
            new ComboProbabilityResult(index, combo.Name, unionEvaluator.Union(ownedEvents[index]), combo.GroupId)).ToList();
        var groupProbabilities = (groups ?? []).Select(group => {
            var members = ownedEvents.Where((_, index) => combos[index].GroupId == group.Id).ToList();
            return new GroupProbabilityResult(group.Id, group.Name, unionEvaluator.Union(members.SelectMany(e => e).ToList()), members.Count);
        }).ToList();

        return new ProbabilityCalculationResult(
            totalProbability, comboProbabilities.AsReadOnly(), groupProbabilities.AsReadOnly());
    }
}
