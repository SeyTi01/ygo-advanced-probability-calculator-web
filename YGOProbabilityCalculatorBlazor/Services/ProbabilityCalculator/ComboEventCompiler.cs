using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

// Expands routes and compiles each combo into hand-wide bounds using Hall's condition.
internal sealed class ComboEventCompiler(List<Card> deck, int handSize, WorkBudget budget) {
    private long _compiledEntries;
    private long _compiledCells;
    private long _selectorCells;
    private readonly Dictionary<string, BigInteger> _selectorMasks = new(StringComparer.Ordinal);

    internal List<CompiledEvent?> CompileCombo(Combo combo) {
        // Reduce exact repeated groups before checking the remaining product.
        // This request-wide bound is independent of the 30 USER combo policy.
        long routes = 1;
        budget.Spend(combo.RequirementCount + 1L);
        IReadOnlyList<ComboAlternativeGroup> groups = ComboAlternativeReducer.Reduce(combo.AlternativeGroups, budget);

        foreach (ComboAlternativeGroup group in groups) {
            budget.Spend(group.Alternatives.Count + 1L);

            if (routes > budget.StoragePolicy.EntryLimit / group.Alternatives.Count) {
                throw new ProbabilityCalculationLimitException(ProbabilityCalculationLimitReason.Storage);
            }

            routes *= group.Alternatives.Count;
        }

        budget.CheckStorage(_selectorMasks.Count + _compiledEntries + routes, _selectorCells + combo.Categories.Count + (long)combo.Cards.Count + groups.Count);
        int[] choices = new int[groups.Count];
        HashSet<CompiledEvent> unique = [];

        for (long route = 0; route < routes; route++) {
            budget.Spend(combo.Categories.Count + (long)combo.Cards.Count + choices.Length + 1);
            List<ComboCategory> categories = [.. combo.Categories];
            List<ComboCard> cards = [.. combo.Cards];

            for (int g = 0; g < choices.Length; g++) {
                ComboAlternative alternative = groups[g].Alternatives[choices[g]];

                if (alternative.Category is { } category) {
                    categories.Add(category);
                }
                else {
                    cards.Add(alternative.Card!);
                }
            }

            CompiledEvent? compiled = Canonicalize(new Combo(categories, cards: cards));

            if (compiled is not null) {
                budget.Spend(compiled.Constraints.Length + 1L);

                if (!unique.Contains(compiled)) {
                    long cells = (long)(compiled.Constraints.Length + (compiled.Roles?.Length ?? 0)) * (3 + deck.Count / 32);
                    budget.CheckStorage(_selectorMasks.Count + _compiledEntries + 1, _selectorCells + _compiledCells + cells);
                    _compiledEntries++;
                    _compiledCells += cells;
                    unique.Add(compiled);
                }
            }

            for (int g = choices.Length - 1; g >= 0; g--) {
                if (++choices[g] < groups[g].Alternatives.Count) {
                    break;
                }

                choices[g] = 0;
            }
        }

        return [.. unique];
    }

    private CompiledEvent? Canonicalize(Combo combo) {
        budget.Spend(combo.Categories.Count + combo.Cards.Count + 1L);
        List<Requirement> constraints = [];

        foreach (IGrouping<string, ComboCategory> group in combo.Categories.GroupBy(static category => category.Selector.Signature, StringComparer.Ordinal)) {
            int min = group.Max(static category => category.MinCount);
            int max = Math.Min(handSize, group.Min(category => category.GetEffectiveMaximum(handSize)));

            if (min > max) {
                return null;
            }

            // Every category count is in [0, handSize], regardless of overlap.
            if (min == 0 && max == handSize) {
                continue;
            }

            constraints.Add(new Requirement(new ConstraintKey(false, group.Key), min, max, group.First().Selector));
        }

        foreach (IGrouping<string, ComboCard> group in combo.Cards.GroupBy(static card => card.CardId, StringComparer.Ordinal)) {
            int min = group.Max(static card => card.MinCount);
            int max = Math.Min(handSize, group.Min(card => card.GetEffectiveMaximum(handSize)));

            if (min > max) {
                return null;
            }

            if (min == 0 && max == handSize) {
                continue;
            }

            constraints.Add(new Requirement(new ConstraintKey(true, group.Key), min, max));
        }

        if (constraints.Sum(static constraint => (long)constraint.MinCount) > handSize) {
            return null;
        }

        List<CountBound> roles = [];

        foreach (Requirement constraint in constraints) {
            BigInteger eligible = BigInteger.Zero;

            if (constraint.Selector is { Kind: not CardSelectorKind.Category }) {
                if (!_selectorMasks.TryGetValue(constraint.Key.Value, out eligible)) {
                    CardSelector selector = constraint.Selector!;
                    long cells = selector.Signature.Length + 1L + deck.Count / 32;
                    budget.Spend(selector.Signature.Length + 1L);
                    budget.CheckStorage(_selectorMasks.Count + 1L + _compiledEntries, _selectorCells + _compiledCells + cells);
                    _selectorCells += cells;
                    bool[] matching = CardSelectorCompiler.Compile(selector, deck, budget);
                    for (int row = 0; row < matching.Length; row++) {
                        if (matching[row] && deck[row].Copies > 0) {
                            eligible |= BigInteger.One << row;
                        }
                    }
                    _selectorMasks.Add(constraint.Key.Value, eligible);
                }
                roles.Add(new CountBound(eligible, constraint.MinCount, constraint.MaxCount));
                continue;
            }

            for (int i = 0; i < deck.Count; i++) {
                budget.Spend(deck[i].Categories.Count + 1L);

                bool isEligibleCopy = deck[i].Copies > 0 && (constraint.Key.IsCard
                    ? deck[i].Id == constraint.Key.Value
                    : deck[i].Categories.Any(category => category.Identity == constraint.Selector!.CategoryIdentities[0]));

                if (isEligibleCopy) {
                    eligible |= BigInteger.One << i;
                }
            }

            roles.Add(new CountBound(eligible, constraint.MinCount, constraint.MaxCount));
        }

        return CompileRoles([.. roles]);
    }

    internal CompiledEvent? CompileRoles(CountBound[] roles) {
        // Hall's matching condition with repeated slots: every subset of roles
        // needs at least the sum of its minima in the UNION of eligible copies.
        // Whole roles suffice: partial slots have the same neighbors and no
        // greater demand. Upper bounds still count the entire hand.
        Dictionary<BigInteger, (int Min, int Max)> bounds = new();
        Dictionary<BigInteger, int> demands = new() { [BigInteger.Zero] = 0 };

        foreach (CountBound constraint in roles) {
            BigInteger eligible = constraint.EligibleRows;

            if (!AddBound(eligible, constraint.MinCount, constraint.MaxCount)) {
                return null;
            }

            if (constraint.MinCount == 0) {
                continue;
            }

            foreach ((BigInteger subset, int demand) in demands.ToArray()) {
                budget.Spend(1 + eligible.GetBitLength() / 32);
                BigInteger union = subset | eligible;
                int minimum = demand + constraint.MinCount;
                demands[union] = Math.Max(demands.GetValueOrDefault(union), minimum);
                budget.CheckStorage(demands.Count, (long)demands.Count * (1 + deck.Count / 32));
            }
        }

        foreach ((BigInteger eligible, int minimum) in demands) {
            if (!AddBound(eligible, minimum, handSize)) {
                return null;
            }
        }

        CountBound[] compiledBounds = bounds
            .OrderBy(static pair => pair.Key)
            .Select(static pair => new CountBound(pair.Key, pair.Value.Min, pair.Value.Max))
            .ToArray();

        return new CompiledEvent(compiledBounds, roles.All(role => role.MaxCount == handSize) ? roles : null);

        bool AddBound(BigInteger eligible, int min, int max) {
            budget.Spend(1 + eligible.GetBitLength() / 32);

            if (eligible.IsZero) {
                return min == 0;
            }

            if (bounds.TryGetValue(eligible, out (int Min, int Max) previous)) {
                min = Math.Max(min, previous.Min);
                max = Math.Min(max, previous.Max);
            }

            if (min > max) {
                return false;
            }

            bounds[eligible] = (min, max);
            budget.CheckStorage(bounds.Count, (long)bounds.Count * (1 + deck.Count / 32));

            return true;
        }
    }

    private readonly record struct ConstraintKey(bool IsCard, string Value);

    private readonly record struct Requirement(ConstraintKey Key, int MinCount, int MaxCount, CardSelector? Selector = null);
}
