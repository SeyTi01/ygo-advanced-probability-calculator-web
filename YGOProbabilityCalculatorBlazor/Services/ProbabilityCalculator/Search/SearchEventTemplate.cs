using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Compilation;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Search;

// One ordered counting pass owns its immutable search plan, combo definitions
// and expanded row order. Compilation observes no held counts: those are applied
// later by ScenarioConditioner and the pool constraints. Keep only one template,
// so the cache cannot grow with the terminal distribution or outlive the pass.
internal sealed class SearchEventTemplate {
    private readonly SearchAccessPlan _search;
    private readonly List<Combo> _combos;
    private readonly List<Card> _rows;
    private readonly WorkBudget _budget;
    private readonly int _randomRows;
    private List<List<CompiledEvent?>>? _events;
    private (int Hand, int Capacity, int Loss) _key;

    internal SearchEventTemplate(SearchAccessPlan search, List<Combo> combos, List<Card> rows, int randomRows, WorkBudget budget) {
        _search = search;
        _combos = combos;
        _rows = rows;
        _budget = budget;
        _randomRows = randomRows;
        budget.Spend(rows.Count + 1L);
        budget.CheckStorage(rows.Count, rows.Count);
    }

    internal List<List<CompiledEvent?>> Compile(int handSize, int capacity, int loss) {
        _budget.Spend(1);
        // SearchAccessPlan uses capacity only to bound the number of substitutions.
        // With no substitutions, loss cannot change any projected constraint.
        int limit = Math.Min(handSize, Math.Max(0, capacity));
        (int Hand, int Capacity, int Loss) key = (handSize, limit, limit == 0 ? 0 : loss);
        if (_events is not null && key == _key) {
            _budget.SearchDiagnostics?.Add("template hits");

            return _events;
        }
        // Release the old template before compiling; never retain a second cache
        // entry. Compile checks the complete predicate payload against Storage.
        _budget.SearchDiagnostics?.Template(key);
        _events = null;
        _key = key;
        _events = _search.Compile(_combos, handSize, _rows, limit, key.Loss);
        BigInteger randomMask = (BigInteger.One << _randomRows) - 1;
        foreach (List<CompiledEvent?> routes in _events) {
            for (int route = 0; route < routes.Count; route++) {
                if (routes[route] is not { } compiled) {
                    continue;
                }
                _budget.Spend((compiled.Constraints.Length + 1L) * (compiled.Constraints.Length + 1L) * (1L + _rows.Count / 32));
                // A conjunction is order independent. Resolve fully observed
                // bounds first, especially small positive masks (held sources),
                // before constructing residual constraints for conditional pools.
                routes[route] = new(compiled.Constraints
                    .OrderBy(bound => (bound.EligibleRows & randomMask).IsZero ? 0 : 1)
                    .ThenBy(bound => bound.MinCount > 0 ? 0 : 1)
                    .ThenBy(bound => BigInteger.PopCount(bound.EligibleRows)).ToArray());
            }
        }

        return _events;
    }
}
