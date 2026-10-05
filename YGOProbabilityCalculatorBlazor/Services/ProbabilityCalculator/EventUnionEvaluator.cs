using System.Numerics;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

// Evaluates exact unions of independently compiled events; intersections never
// create distinct-copy assignments across different combos.
internal sealed class EventUnionEvaluator(
    int deckRowCount, int handSize, WorkBudget budget, ComboEventCompiler compiler, ExactHandCounter counter) {
    internal double Union(List<CompiledEvent?> events) {
        budget.Spend(events.Count + 1L);
        var universal = events.FirstOrDefault(e => e is { Constraints.Length: 0 });
        if (universal is not null) return counter.Probability(universal);
        var terms = new Dictionary<CompiledEvent, BigInteger>();
        long storedConstraints = 0;
        foreach (var current in FactorAlternatives(events)) {
            // Indicator identity: U OR E = U + E - U*E. Equal intersections
            // combine integer coefficients before any floating-point evaluation.
            // Nested events cancel here too; no independence assumption is used.
            var changes = new Dictionary<CompiledEvent, BigInteger>();
            long changedConstraints = 0;
            Add(changes, current, 1, ref changedConstraints);
            foreach (var (existing, coefficient) in terms) {
                var intersection = Intersect(existing, current);
                if (intersection is not null)
                    Add(changes, intersection, -coefficient, ref changedConstraints);
            }
            foreach (var (intersection, coefficient) in changes)
                Add(terms, intersection, coefficient, ref storedConstraints);
        }

        if (terms.Count == 0) return 0;
        BigInteger successes = 0;
        foreach (var (intersection, coefficient) in terms) {
            var count = counter.Count(intersection);
            budget.Spend(1 + count.GetBitLength() / 64 + successes.GetBitLength() / 64 + coefficient.GetBitLength() / 64);
            successes += coefficient * count;
            WorkBudget.CheckStorage(1, WorkBudget.IntegerCells(successes));
        }
        // Cancel signed inclusion-exclusion terms exactly, before conversion.
        return counter.ToProbability(successes);
    }

    private List<CompiledEvent> FactorAlternatives(List<CompiledEvent?> events) {
        foreach (var item in events) budget.Spend((item?.Constraints.Length ?? 0) + 1L);
        var alternatives = events.OfType<CompiledEvent>().Distinct().ToList();
        for (var i = 0; i < alternatives.Count; i++) {
            for (var j = i + 1; j < alternatives.Count; j++) {
                budget.Spend(1);
                var first = alternatives[i].Roles;
                var second = alternatives[j].Roles;
                if (first is null || second is null || first.Length != second.Length) continue;
                // Multiset subtraction preserves separate slots even when a
                // direct card and a category have identical eligibility.
                var unmatched = second.ToList();
                var common = new List<CountBound>();
                CountBound? different = null;
                foreach (var role in first) {
                    budget.Spend((long)(unmatched.Count + 1) * (1 + deckRowCount / 32));
                    var match = unmatched.IndexOf(role);
                    if (match >= 0) { common.Add(role); unmatched.RemoveAt(match); }
                    else if (different is null) different = role;
                    else break;
                }
                if (common.Count != first.Length - 1 || unmatched.Count != 1 ||
                    different is not { MinCount: 1 } left || unmatched[0].MinCount != 1) continue;

                // With no restrictive hand-wide maxima, C+A OR C+B is
                // C+(A union B) for ONE alternative slot: its assigned copy
                // belongs to at least one branch, with the same assignment
                // for C. The converse is immediate. This fails for demand>1
                // (copies could split across branches) or branch maxima.
                common.Add(new CountBound(left.EligibleRows | unmatched[0].EligibleRows, 1, handSize));
                alternatives[i] = compiler.CompileRoles(common.ToArray())!;
                alternatives.RemoveAt(j);
                // A merge can expose another common-role pair anywhere.
                i = -1;
                break;
            }
        }
        return alternatives;
    }

    private void Add(Dictionary<CompiledEvent, BigInteger> terms, CompiledEvent key, BigInteger coefficient, ref long storedConstraints) {
        budget.Spend(key.Constraints.Length + 1L + coefficient.GetBitLength() / 32);
        if (terms.TryGetValue(key, out var previous)) {
            budget.Spend(1 + previous.GetBitLength() / 32);
            var updated = previous + coefficient;
            var cells = storedConstraints - WorkBudget.IntegerCells(previous) + WorkBudget.IntegerCells(updated);
            WorkBudget.CheckStorage(terms.Count, cells);
            if (updated == 0) {
                terms.Remove(key);
                storedConstraints -= key.Constraints.Length + WorkBudget.IntegerCells(previous);
            } else { terms[key] = updated; storedConstraints = cells; }
        } else if (coefficient != 0) {
            WorkBudget.CheckStorage(terms.Count + 1, storedConstraints + key.Constraints.Length + WorkBudget.IntegerCells(coefficient));
            terms.Add(key, coefficient);
            storedConstraints += key.Constraints.Length + WorkBudget.IntegerCells(coefficient);
        }
    }

    private CompiledEvent? Intersect(CompiledEvent first, CompiledEvent second) {
        // These are hand-wide count bounds already compiled separately for
        // each combo. Conjoin them; never create Hall subsets across combos,
        // since each combo may reuse the same drawn copies independently.
        budget.Spend(first.Constraints.Length + second.Constraints.Length + 1L);
        var constraints = new List<CountBound>();
        int i = 0, j = 0;
        while (i < first.Constraints.Length || j < second.Constraints.Length) {
            if (i == first.Constraints.Length) { constraints.Add(second.Constraints[j++]); continue; }
            if (j == second.Constraints.Length) { constraints.Add(first.Constraints[i++]); continue; }
            var left = first.Constraints[i];
            var right = second.Constraints[j];
            var order = left.EligibleRows.CompareTo(right.EligibleRows);
            if (order < 0) { constraints.Add(left); i++; }
            else if (order > 0) { constraints.Add(right); j++; }
            else {
                var min = Math.Max(left.MinCount, right.MinCount);
                var max = Math.Min(left.MaxCount, right.MaxCount);
                if (min > max) return null;
                constraints.Add(new CountBound(left.EligibleRows, min, max));
                i++; j++;
            }
        }
        // A disjoint collection of eligible row sets consumes at least the
        // sum of its hand-wide minima. A greedy collection is sufficient
        // for a sound rejection; it need not find every impossible event.
        // This is NOT matching roles across combos: intersected bounds
        // already describe counts in the same hand.
        var occupied = BigInteger.Zero;
        long required = 0;
        foreach (var bound in constraints) {
            budget.Spend(1 + bound.EligibleRows.GetBitLength() / 32);
            if (bound.MinCount == 0 || (occupied & bound.EligibleRows) != 0) continue;
            occupied |= bound.EligibleRows;
            required += bound.MinCount;
            if (required > handSize) return null;
        }
        return new CompiledEvent(constraints.ToArray());
    }
}
