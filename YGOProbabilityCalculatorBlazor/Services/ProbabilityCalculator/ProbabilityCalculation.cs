using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

// One instance per service call: compilation storage, count caches and the shared
// work budget remain local to this request, including total/combo/group unions.
internal sealed class ProbabilityCalculation
{
    private readonly ComboEventCompiler compiler;
    private readonly EventUnionEvaluator unionEvaluator;

    internal ProbabilityCalculation(List<Card> deck, int handSize, CalculationWorkPolicy workPolicy)
    {
        WorkBudget budget = new(workPolicy);
        compiler = new(deck, handSize, budget);
        ExactHandCounter counter = new(deck, handSize, budget);
        unionEvaluator = new(deck.Count, handSize, budget, compiler, counter);
    }

    internal double CalculateUnion(List<Combo> combos) =>
        unionEvaluator.Union([.. combos.SelectMany(combo => compiler.CompileCombo(combo))]);

    internal ProbabilityCalculationResult CalculateResults(List<Combo> combos, IReadOnlyList<ComboGroup>? groups)
    {
        List<List<CompiledEvent?>> ownedEvents = [.. combos.Select(combo => compiler.CompileCombo(combo))];
        List<CompiledEvent?> events = [.. ownedEvents.SelectMany(e => e)];
        double totalProbability = unionEvaluator.Union(events);
        List<ComboProbabilityResult> comboProbabilities =
        [
            .. combos.Select((combo, index) =>
                new ComboProbabilityResult(index, combo.Name, unionEvaluator.Union(ownedEvents[index]), combo.GroupId))
        ];
        List<GroupProbabilityResult> groupProbabilities =
        [
            .. (groups ?? []).Select(group =>
            {
                List<List<CompiledEvent?>> members =
                    [.. ownedEvents.Where((_, index) => combos[index].GroupId == group.Id)];

                return new GroupProbabilityResult(group.Id,
                    group.Name,
                    unionEvaluator.Union([.. members.SelectMany(e => e)]),
                    members.Count);
            })
        ];

        return new ProbabilityCalculationResult(
            totalProbability,
            comboProbabilities.AsReadOnly(),
            groupProbabilities.AsReadOnly());
    }
}
