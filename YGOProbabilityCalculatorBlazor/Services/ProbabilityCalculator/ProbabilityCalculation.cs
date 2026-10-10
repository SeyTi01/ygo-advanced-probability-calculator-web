using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

internal sealed record ExactCalculationResult(
    ExactProbability Total,
    ExactProbability[] Combos,
    ExactProbability[] Groups
) {
    internal ProbabilityCalculationResult ToPublic(List<Combo> combos, IReadOnlyList<ComboGroup>? groups, WorkBudget budget) {
        List<ComboProbabilityResult> comboResults = combos.Select((combo, index) =>
            new ComboProbabilityResult(index, combo.Name, Combos[index].ToDouble(budget), combo.GroupId)).ToList();
        List<GroupProbabilityResult> groupResults = (groups ?? []).Select((group, index) =>
            new GroupProbabilityResult(group.Id, group.Name, Groups[index].ToDouble(budget),
                combos.Count(combo => combo.GroupId == group.Id))).ToList();
        return new(Total.ToDouble(budget), comboResults.AsReadOnly(), groupResults.AsReadOnly());
    }
}

// One fixed sample space. Scenario instances share the request's cumulative budget,
// but release their compilation/count caches before evaluating the next scenario.
internal sealed class ProbabilityCalculation {
    private readonly ComboEventCompiler _compiler;
    private readonly EventUnionEvaluator _unionEvaluator;
    private readonly ScenarioConditioner? _conditioner;
    private readonly WorkBudget _budget;

    internal ProbabilityCalculation(List<Card> deck, int handSize, CalculationWorkPolicy workPolicy)
        : this(deck, handSize, new WorkBudget(workPolicy)) { }

    internal ProbabilityCalculation(List<Card> deck, int handSize, WorkBudget budget, List<Card>? retained = null) {
        _budget = budget;
        List<Card> compilationDeck = retained is null ? deck : [.. deck, .. retained];
        int finalHandSize = checked(handSize + (retained?.Sum(card => card.Copies) ?? 0));
        _compiler = new(compilationDeck, finalHandSize, budget);
        ExactHandCounter counter = new(deck, handSize, budget);
        _unionEvaluator = new(deck.Count, handSize, budget, _compiler, counter);
        if (retained is not null) {
            _conditioner = new(deck.Count, handSize, retained, budget);
        }
    }

    internal double CalculateUnion(List<Combo> combos) => CalculateUnionExact(combos).ToDouble(_budget);

    internal ExactProbability CalculateUnionExact(List<Combo> combos) =>
        _unionEvaluator.UnionExact(combos.SelectMany(Compile).ToList());

    internal ProbabilityCalculationResult CalculateResults(List<Combo> combos, IReadOnlyList<ComboGroup>? groups) =>
        CalculateExactResults(combos, groups).ToPublic(combos, groups, _budget);

    internal ExactCalculationResult CalculateExactResults(List<Combo> combos, IReadOnlyList<ComboGroup>? groups) {
        List<List<CompiledEvent?>> ownedEvents = combos.Select(Compile).ToList();
        ExactProbability total = _unionEvaluator.UnionExact(ownedEvents.SelectMany(events => events).ToList());
        ExactProbability[] individual = ownedEvents.Select(_unionEvaluator.UnionExact).ToArray();
        ExactProbability[] grouped = (groups ?? []).Select(group => _unionEvaluator.UnionExact(
            ownedEvents.Where((_, index) => combos[index].GroupId == group.Id).SelectMany(events => events).ToList())).ToArray();
        return new(total, individual, grouped);
    }

    private List<CompiledEvent?> Compile(Combo combo) {
        List<CompiledEvent?> events = _compiler.CompileCombo(combo);
        return _conditioner is null ? events : events.Select(_conditioner.Project).ToList();
    }
}
