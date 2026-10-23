using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Compilation;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Counting;

// Terminal-only, bounded search phase. Random outcomes are counted by the caller;
// every legal witness contributes only a Boolean for each result row. Original
// and fetched copies remain separate so a fetched effect can never activate.
internal sealed class BoundedSearchHandCounter {
    private readonly List<Card> _cards;
    private readonly List<Combo> _combos;
    private readonly WorkBudget _budget;
    private readonly bool[] _relevant;
    private readonly (int Source, int Limit, int[] Targets)[] _modes;
    private readonly record struct HandBound(int[] Rows, int Minimum, int Maximum);
    private readonly Dictionary<int, List<List<HandBound[]?>>> _events = [];
    private long _eventCells;

    internal BoundedSearchHandCounter(List<Card> cards, List<Combo> combos, WorkBudget budget) {
        _cards = cards;
        _combos = combos;
        _budget = budget;
        budget.Spend(cards.Sum(card => card.Categories.Count + 1L)
            * (combos.Sum(combo => combo.AllCategories.Sum(requirement => (long)requirement.Selector.EvaluationSize)
                + combo.AllCards.Count() + 1L) + cards.Sum(card => (card.Search?.Selector.EvaluationSize ?? 0) + 1L)));
        _relevant = cards.Select(card => combos.Any(combo =>
            combo.AllCards.Any(requirement => requirement.MinCount > 0 && requirement.CardId == card.Id)
            || combo.AllCategories.Any(requirement => requirement.MinCount > 0 && requirement.Selector.Matches(card)))).ToArray();
        List<(int, int, int[])> modes = [];
        for (int source = 0; source < cards.Count; source++) {
            if (cards[source].Search is not { } search) {
                continue;
            }
            foreach (SearchEffectMode mode in search.Modes ?? [new(search.Selector)]) {
                int[] targets = Enumerable.Range(0, cards.Count)
                    .Where(target => target != source && search.Matches(cards[target], mode.Selector)).ToArray();
                modes.Add((source, mode.ActivationLimit ?? int.MaxValue, targets));
            }
        }
        _modes = modes.ToArray();
        budget.CheckStorage(_modes.Length + cards.Count, _modes.Sum(mode => (long)mode.Targets.Length + 2) + cards.Count);
    }

    internal bool[] Evaluate(ReadOnlySpan<int> initial, int[] deck) {
        int[] hand = initial.ToArray();
        int[] original = initial.ToArray();
        int[] used = new int[_cards.Count];
        int[] modeUses = new int[_modes.Length];
        bool[] success = new bool[_combos.Count];
        Visit(0);

        return success;

        void Visit(int depth) {
            _budget.Spend(hand.Length + _modes.Length + 1L);
            _budget.CheckStorage(depth + 1L, (depth + 1L) * (4L * hand.Length + modeUses.Length + 1));
            int size = hand.Sum();
            if (!_events.TryGetValue(size, out List<List<HandBound[]?>>? events)) {
                ComboEventCompiler compiler = new(_cards, size, _budget);
                events = [];
                _eventCells += 1 + _combos.Count;
                foreach (Combo combo in _combos) {
                    List<HandBound[]?> routes = [];
                    foreach (CompiledEvent? compiled in compiler.CompileCombo(combo)) {
                        _eventCells++;
                        if (compiled is null) {
                            routes.Add(null);
                            continue;
                        }
                        HandBound[] bounds = new HandBound[compiled.Constraints.Length];
                        for (int index = 0; index < bounds.Length; index++) {
                            CountBound bound = compiled.Constraints[index];
                            _budget.Spend(_cards.Count * (WorkBudget.IntegerCells(bound.EligibleRows) + 1));
                            int[] rows = Enumerable.Range(0, _cards.Count)
                                .Where(row => (bound.EligibleRows & (BigInteger.One << row)) != 0).ToArray();
                            _eventCells += rows.Length + 3;
                            _budget.CheckStorage(_events.Count + 1L, _eventCells);
                            bounds[index] = new(rows, bound.MinCount, bound.MaxCount);
                        }
                        routes.Add(bounds);
                    }
                    events.Add(routes);
                }
                _events.Add(size, events);
                _budget.CheckStorage(_events.Count, _eventCells);
            }
            for (int combo = 0; combo < success.Length; combo++) {
                if (!success[combo]) {
                    success[combo] = events[combo].Any(route => route is not null && route.All(Matches));
                }
            }
            if (success.All(value => value)) {
                return;
            }
            for (int modeIndex = 0; modeIndex < _modes.Length; modeIndex++) {
                (int source, int limit, int[] targets) = _modes[modeIndex];
                Card card = _cards[source];
                if (original[source] == 0 || used[source] >= card.MaximumActivations
                    || modeUses[modeIndex] >= limit || size < card.MinimumHandSize) {
                    continue;
                }
                foreach (int target in targets) {
                    _budget.Spend(hand.Length * 3L + 1);
                    // Legality is established before either resolution removal.
                    if (deck[target] == 0 || size - 1 < card.PreDrawRemovalCount) {
                        continue;
                    }
                    int[] savedHand = (int[])hand.Clone();
                    int[] savedOriginal = (int[])original.Clone();
                    hand[source]--;
                    original[source]--;
                    used[source]++;
                    modeUses[modeIndex]++;
                    bool legal = Remove(card.PreDrawRemovalCount, card.PreDrawSelector);
                    if (legal) {
                        deck[target]--;
                        hand[target]++;
                        if (Remove(card.PostDrawDiscardCount, card.PostDrawSelector)) {
                            Visit(depth + 1);
                        }
                        deck[target]++;
                    }
                    savedHand.CopyTo(hand, 0);
                    savedOriginal.CopyTo(original, 0);
                    used[source]--;
                    modeUses[modeIndex]--;
                }
            }
        }

        bool Remove(int count, CardSelector selector) {
            for (int removal = 0; removal < count; removal++) {
                int selected = -1;
                int worst = 0;
                int capacity = deck.Sum();
                for (int row = 0; row < hand.Length; row++) {
                    _budget.Spend((selector.EvaluationSize + 1L) * (_cards[row].Categories.Count + 1));
                    if (hand[row] == 0 || !selector.Matches(_cards[row])) {
                        continue;
                    }
                    // Draws have finished. Keep positive pieces, then plausible
                    // still-original searches. Equal tiers retain the established
                    // canonical row tie policy (remove the last row).
                    int tier = _relevant[row] ? 1 : 4;
                    if (capacity > 0 && original[row] > 0 && used[row] < _cards[row].MaximumActivations) {
                        for (int mode = 0; mode < _modes.Length; mode++) {
                            if (_modes[mode].Source != row || modeUses[mode] >= _modes[mode].Limit) {
                                continue;
                            }
                            foreach (int target in _modes[mode].Targets) {
                                _budget.Spend(1);
                                if (_cards[target].Copies > hand[target]) {
                                    tier = Math.Min(tier, _relevant[target] ? 1 : 2);
                                }
                            }
                        }
                    }
                    if (tier >= worst) {
                        selected = row;
                        worst = tier;
                    }
                }
                if (selected < 0) {
                    return false;
                }
                hand[selected]--;
                // Equivalent fetched copies are inert; preserve a usable original
                // copy when both labels of the same entry are held.
                original[selected] = Math.Min(original[selected], hand[selected]);
            }

            return true;
        }

        bool Matches(HandBound bound) {
            // The immutable row mask is decoded once per compiled hand size.
            // This is the same Hall/count predicate, including empty masks and
            // inclusive zero/max bounds; no physical identities are identified.
            _budget.Spend(bound.Rows.Length + 1L);
            int count = 0;
            foreach (int row in bound.Rows) {
                count += hand[row];
            }

            return count >= bound.Minimum && count <= bound.Maximum;
        }
    }
}
