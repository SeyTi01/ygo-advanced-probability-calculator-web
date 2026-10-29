using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Certification;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Compilation;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Counting;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.DrawResolution;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

// One fixed sample space. Scenario instances share the request's cumulative budget,
// but release their compilation/count caches before evaluating the next scenario.
internal sealed class ProbabilityCalculation {
    internal static CalculationCapability Capability => CalculationCapability.NoSearch(SolverRoute.Ordinary);
    private readonly ComboEventCompiler _compiler;
    private readonly EventUnionEvaluator _unionEvaluator;
    private readonly ScenarioConditioner? _conditioner;
    private readonly WorkBudget _budget;

    internal ProbabilityCalculation(
        List<Card> deck,
        int handSize,
        CalculationWorkPolicy workPolicy,
        CalculationStoragePolicy? storagePolicy = null
    ) : this(deck, handSize, new WorkBudget(workPolicy, storagePolicy)) { }

    internal ProbabilityCalculation(List<Card> deck, int handSize, WorkBudget budget, List<Card>? retained = null) {
        _budget = budget;
        bool hasRetainedCards = retained is { Count: > 0 };
        List<Card> compilationDeck = hasRetainedCards ? [.. deck, .. retained!] : deck;
        int finalHandSize = checked(handSize + (retained?.Sum(card => card.Copies) ?? 0));
        _compiler = new(compilationDeck, finalHandSize, budget);
        ExactHandCounter counter = new(deck, handSize, budget);
        _unionEvaluator = new(deck.Count, handSize, budget, _compiler, counter);
        // An empty fixed hand needs no projection. Keeping the compiled roles
        // enables exact alternative factoring before union/count evaluation.
        // With retained copies, only the projected Hall bounds remain valid.
        if (hasRetainedCards) {
            _conditioner = new(deck.Count, handSize, retained!, budget);
        }
    }

    internal double CalculateUnion(List<Combo> combos) => CalculateUnionExact(combos).ToDouble(_budget);

    internal ExactProbability CalculateUnionExact(List<Combo> combos) =>
        _unionEvaluator.UnionExact(combos.SelectMany(Compile).ToList());

    internal ProbabilityCalculationResult CalculateResults(List<Combo> combos, IReadOnlyList<ComboGroup>? groups) =>
        CalculateExactResults(combos, groups).ToPublic(combos, groups, _budget);

    internal ExactCalculationResult CalculateExactResults(List<Combo> combos, IReadOnlyList<ComboGroup>? groups) {
        CalculationResultRows rows = new(combos, groups);
        List<List<CompiledEvent?>> ownedEvents = combos.Select(Compile).ToList();
        ExactProbability total = _unionEvaluator.UnionExact(ownedEvents.SelectMany(events => events).ToList());
        ExactProbability[] individual = ownedEvents.Select(_unionEvaluator.UnionExact).ToArray();
        ExactProbability[] grouped = (groups ?? []).Select((group, groupIndex) => _unionEvaluator.UnionExact(
            ownedEvents.Where((_, index) => rows.GroupContains(groupIndex, index)).SelectMany(events => events).ToList())).ToArray();
        return new(total, individual, grouped);
    }

    private List<CompiledEvent?> Compile(Combo combo) {
        List<CompiledEvent?> events = _compiler.CompileCombo(combo);
        return _conditioner is null ? events : events.Select(_conditioner.Project).ToList();
    }
}
