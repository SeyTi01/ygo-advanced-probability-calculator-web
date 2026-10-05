using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

// Expands routes and compiles each combo into hand-wide bounds using Hall's condition.
internal sealed class ComboEventCompiler(List<Card> deck, int handSize, WorkBudget budget) {
    private long compiledEntries;
    private long compiledCells;

    internal List<CompiledEvent?> CompileCombo(Combo combo) {
        // Check the compact input and potential expansion before route allocation.
        // This request-wide bound is independent of the 30 USER combo policy.
        long routes = 1;
        budget.Spend(combo.RequirementCount + 1L);
        foreach (var group in combo.AlternativeGroups) {
            budget.Spend(group.Alternatives.Count + 1L);
            if (routes > 32768 / group.Alternatives.Count)
                throw new ProbabilityCalculationLimitException(ProbabilityCalculationLimitReason.Storage);
            routes *= group.Alternatives.Count;
        }
        WorkBudget.CheckStorage(compiledEntries + routes,
            combo.Categories.Count + (long)combo.Cards.Count + combo.AlternativeGroups.Count);
        var choices = new int[combo.AlternativeGroups.Count];
        var unique = new HashSet<CompiledEvent>();
        for (long route = 0; route < routes; route++) {
            budget.Spend(combo.Categories.Count + (long)combo.Cards.Count + choices.Length + 1);
            var categories = combo.Categories.ToList();
            var cards = combo.Cards.ToList();
            for (var g = 0; g < choices.Length; g++) {
                var alternative = combo.AlternativeGroups[g].Alternatives[choices[g]];
                if (alternative.Category is { } category) categories.Add(category);
                else cards.Add(alternative.Card!);
            }
            var compiled = Canonicalize(new Combo(categories, cards: cards));
            if (compiled is not null) {
                budget.Spend(compiled.Constraints.Length + 1L);
                if (!unique.Contains(compiled)) {
                    var cells = (long)(compiled.Constraints.Length + (compiled.Roles?.Length ?? 0)) * (3 + deck.Count / 32);
                    WorkBudget.CheckStorage(compiledEntries + 1, compiledCells + cells);
                    compiledEntries++;
                    compiledCells += cells;
                    unique.Add(compiled);
                }
            }
            for (var g = choices.Length - 1; g >= 0; g--) {
                if (++choices[g] < combo.AlternativeGroups[g].Alternatives.Count) break;
                choices[g] = 0;
            }
        }
        return unique.Cast<CompiledEvent?>().ToList();
    }

    private CompiledEvent? Canonicalize(Combo combo) {
        budget.Spend(combo.Categories.Count + combo.Cards.Count + 1L);
        var constraints = new List<Requirement>();
        foreach (var group in combo.Categories.GroupBy(c => c.BaseCategory.Identity, StringComparer.Ordinal)) {
            var min = group.Max(c => c.MinCount);
            var max = Math.Min(handSize, group.Min(c => c.GetEffectiveMaximum(handSize)));
            if (min > max) return null;
            // Every category count is in [0, handSize], regardless of overlap.
            if (min == 0 && max == handSize) continue;
            constraints.Add(new Requirement(new ConstraintKey(false, group.Key), min, max));
        }
        foreach (var group in combo.Cards.GroupBy(c => c.CardId, StringComparer.Ordinal)) {
            var min = group.Max(c => c.MinCount);
            var max = Math.Min(handSize, group.Min(c => c.GetEffectiveMaximum(handSize)));
            if (min > max) return null;
            if (min == 0 && max == handSize) continue;
            constraints.Add(new Requirement(new ConstraintKey(true, group.Key), min, max));
        }
        if (constraints.Sum(c => (long)c.MinCount) > handSize) return null;

        var roles = new List<CountBound>();
        foreach (var constraint in constraints) {
            var eligible = BigInteger.Zero;
            for (var i = 0; i < deck.Count; i++) {
                budget.Spend(deck[i].Categories.Count + 1L);
                if (deck[i].Copies > 0 && (constraint.Key.IsCard
                    ? deck[i].Id == constraint.Key.Value
                    : deck[i].Categories.Any(c => c.Identity == constraint.Key.Value)))
                    eligible |= BigInteger.One << i;
            }
            roles.Add(new CountBound(eligible, constraint.MinCount, constraint.MaxCount));
        }
        return CompileRoles(roles.ToArray());
    }

    internal CompiledEvent? CompileRoles(CountBound[] roles) {
        // Hall's matching condition with repeated slots: every subset of roles
        // needs at least the sum of its minima in the UNION of eligible copies.
        // Whole roles suffice: partial slots have the same neighbors and no
        // greater demand. Upper bounds still count the entire hand.
        var bounds = new Dictionary<BigInteger, (int Min, int Max)>();
        var demands = new Dictionary<BigInteger, int> { [BigInteger.Zero] = 0 };
        foreach (var constraint in roles) {
            var eligible = constraint.EligibleRows;
            if (!AddBound(eligible, constraint.MinCount, constraint.MaxCount)) return null;
            if (constraint.MinCount == 0) continue;
            foreach (var (subset, demand) in demands.ToArray()) {
                budget.Spend(1 + eligible.GetBitLength() / 32);
                var union = subset | eligible;
                var minimum = demand + constraint.MinCount;
                demands[union] = Math.Max(demands.GetValueOrDefault(union), minimum);
                WorkBudget.CheckStorage(demands.Count, (long)demands.Count * (1 + deck.Count / 32));
            }
        }
        foreach (var (eligible, minimum) in demands)
            if (!AddBound(eligible, minimum, handSize)) return null;
        return new CompiledEvent(bounds.OrderBy(pair => pair.Key)
            .Select(pair => new CountBound(pair.Key, pair.Value.Min, pair.Value.Max)).ToArray(),
            roles.All(r => r.MaxCount == handSize) ? roles : null);

        bool AddBound(BigInteger eligible, int min, int max) {
            budget.Spend(1 + eligible.GetBitLength() / 32);
            if (eligible.IsZero) return min == 0;
            if (bounds.TryGetValue(eligible, out var previous)) {
                min = Math.Max(min, previous.Min);
                max = Math.Min(max, previous.Max);
            }
            if (min > max) return false;
            bounds[eligible] = (min, max);
            WorkBudget.CheckStorage(bounds.Count, (long)bounds.Count * (1 + deck.Count / 32));
            return true;
        }
    }

    private readonly record struct ConstraintKey(bool IsCard, string Value);
    private readonly record struct Requirement(ConstraintKey Key, int MinCount, int MaxCount);
}
