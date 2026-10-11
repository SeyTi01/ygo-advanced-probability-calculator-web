using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

// A layer counts labeled prefixes of equal length. Remaining multiplicities weight
// each next draw. Unseen suffixes contribute (N - depth)!, so all outcomes share N!.
// No physical permutations are enumerated and no probability mass is rounded.
internal sealed class PostDrawCalculation(List<Card> deck, int handSize, WorkBudget budget) {
    internal int TerminalStates { get; private set; }
    internal int PeakStates { get; private set; }

    // Owned immutable vectors: remaining counts, hand counts, used OPT flags,
    // batch draws left, pending discard count, then FIFO candidate entry indices.
    private sealed record State(int[] Values) {
        public bool Equals(State? other) => other is not null && Values.AsSpan().SequenceEqual(other.Values);

        public override int GetHashCode() {
            HashCode hash = new();
            foreach (int value in Values) {
                hash.Add(value);
            }
            return hash.ToHashCode();
        }
    }

    internal ExactCalculationResult Calculate(List<Combo> combos, IReadOnlyList<ComboGroup>? groups, bool totalOnly = false) {
        List<Card> cards = ReduceDeck(combos);
        int n = cards.Count;
        int copies = 0;
        foreach (Card card in cards) {
            budget.Spend(1);
            ArgumentOutOfRangeException.ThrowIfNegative(card.Copies);
            copies = checked(copies + card.Copies);
        }
        if (handSize < 0 || handSize > copies) {
            throw new ArgumentOutOfRangeException(nameof(handSize));
        }

        budget.CheckStorage(1, 3L * n + copies + 3);
        foreach (Combo combo in combos) {
            budget.Spend((combo.AllCards.Count() + combo.AllCategories.Count() + 1L) * (n + cards.Sum(card => (long)card.Categories.Count)));
        }
        bool[] relevant = cards.Select(card => combos.Any(combo =>
            combo.AllCards.Any(requirement => requirement.MinCount > 0 && requirement.CardId == card.Id)
            || combo.AllCategories.Any(requirement => requirement.MinCount > 0 && card.Categories.Contains(requirement.BaseCategory)))).ToArray();

        budget.CheckStorage(copies + 1L, copies + 1L);
        BigInteger[] factorial = new BigInteger[copies + 1];
        factorial[0] = 1;
        long factorialCells = 1;
        for (int i = 1; i <= copies; i++) {
            budget.Spend(WorkBudget.IntegerCells(factorial[i - 1]) + 1);
            factorial[i] = factorial[i - 1] * i;
            factorialCells += WorkBudget.IntegerCells(factorial[i]);
            budget.CheckStorage(copies + 1L, factorialCells);
        }

        int[] initial = new int[3 * n + 2];
        for (int i = 0; i < n; i++) {
            initial[i] = cards[i].Copies;
        }
        initial[3 * n] = handSize;
        Dictionary<State, BigInteger> current = new() { [new(initial)] = 1 };
        Dictionary<State, BigInteger> terminal = new();
        long terminalCells = 0;
        for (int depth = 0; current.Count > 0; depth++) {
            Dictionary<State, BigInteger> next = new();
            long nextCells = 0;
            foreach ((State state, BigInteger weight) in current) {
                budget.Spend(state.Values.Length + 1L);
                int[] values = (int[])state.Values.Clone();
                int remaining = copies - depth;
                if (!Settle(ref values, remaining)) {
                    continue;
                }
                if (values[3 * n] == 0) {
                    budget.Spend(WorkBudget.IntegerCells(weight) * (WorkBudget.IntegerCells(factorial[remaining]) + 1));
                    Add(terminal, new(values.AsSpan(n, n).ToArray()), weight * factorial[remaining], ref terminalCells);
                    continue;
                }
                for (int i = 0; i < n; i++) {
                    budget.Spend(1);
                    if (values[i] == 0) {
                        continue;
                    }
                    bool candidate = cards[i].DrawCount is not null && values[2 * n + i] == 0
                        && (!cards[i].DrawOncePerTurn || !values.AsSpan(3 * n + 2).Contains(i));
                    budget.Spend(values.Length + WorkBudget.IntegerCells(weight) + 1L);
                    int[] drawn = new int[values.Length + (candidate ? 1 : 0)];
                    values.CopyTo(drawn, 0);
                    drawn[i]--;
                    drawn[n + i]++;
                    drawn[3 * n]--;
                    if (candidate) {
                        drawn[^1] = i;
                    }
                    // Normalize immediately so batches differing only in discarded
                    // cards or obsolete candidates merge before the next layer.
                    if (Settle(ref drawn, remaining - 1)) {
                        Add(next, new(drawn), weight * values[i], ref nextCells);
                    }
                }
            }
            PeakStates = Math.Max(PeakStates, next.Count);
            current = next;
        }

        TerminalStates = terminal.Count;
        int resultCount = 1 + (totalOnly ? 0 : combos.Count + (groups?.Count ?? 0));
        long resultIntegerCells = WorkBudget.IntegerCells(factorial[copies]);
        budget.CheckStorage(resultCount, resultCount * (resultIntegerCells + 1));
        BigInteger[] wins = new BigInteger[resultCount];
        // Compile once per final hand size. All result rows see the same hands.
        budget.Spend(terminal.Count * (n + 1L));
        foreach (IGrouping<int, KeyValuePair<State, BigInteger>> size in terminal.GroupBy(pair => pair.Key.Values.Sum())) {
            ComboEventCompiler compiler = new(cards, size.Key, budget);
            List<List<CompiledEvent?>> events = combos.Select(compiler.CompileCombo).ToList();
            foreach ((State hand, BigInteger weight) in size) {
                budget.Spend((wins.Length + (long)combos.Count * (groups?.Count ?? 0)) * (resultIntegerCells + 1));
                bool[] matches = events.Select(routes => routes.Any(route => route is not null && Matches(route, hand.Values))).ToArray();
                if (matches.Any(match => match)) {
                    wins[0] += weight;
                }
                if (!totalOnly) {
                    for (int i = 0; i < combos.Count; i++) {
                        if (matches[i]) {
                            wins[i + 1] += weight;
                        }
                    }
                    for (int g = 0; g < (groups?.Count ?? 0); g++) {
                        if (combos.Where((combo, index) => matches[index] && combo.GroupId == groups![g].Id).Any()) {
                            wins[1 + combos.Count + g] += weight;
                        }
                    }
                }
            }
        }
        ExactProbability[] results = wins.Select(win => new ExactProbability(win, factorial[copies])).ToArray();
        return new(results[0], totalOnly ? [] : results.Skip(1).Take(combos.Count).ToArray(), totalOnly ? [] : results.Skip(1 + combos.Count).ToArray());

        bool Matches(CompiledEvent route, int[] hand) {
            foreach (CountBound bound in route.Constraints) {
                budget.Spend(n * (WorkBudget.IntegerCells(bound.EligibleRows) + 1));
                int count = 0;
                for (int i = 0; i < n; i++) {
                    if ((bound.EligibleRows & (BigInteger.One << i)) != 0) {
                        count += hand[i];
                    }
                }
                if (count < bound.MinCount || count > bound.MaxCount) {
                    return false;
                }
            }
            return true;
        }

        bool Settle(ref int[] values, int remaining) {
            if (values[3 * n] > 0) {
                return values[3 * n] <= remaining;
            }
            for (int discard = values[3 * n + 1]; discard > 0; discard--) {
                // Queue scan, tier scan, removal scan and copying the surviving queue.
                budget.Spend(2L * values.Length + n + 1);
                int selected = -1;
                int worstTier = 0;
                int selectedQueue = -1;
                bool[] pending = new bool[n];
                for (int q = 3 * n + 2; q < values.Length; q++) {
                    int entry = values[q];
                    pending[entry] = true;
                }
                for (int i = 0; i < n; i++) {
                    if (values[n + i] == 0) {
                        continue;
                    }
                    bool usable = pending[i] && values[2 * n + i] == 0 && cards[i].DrawCount <= remaining;
                    bool redundant = cards[i].DrawOncePerTurn && values[n + i] > 1;
                    int tier = (relevant[i], usable && !redundant) switch {
                        (true, _) => 1,
                        (_, true) => 2,
                        _ => 3
                    };
                    if (tier >= worstTier) {
                        worstTier = tier;
                        selected = i;
                    }
                }
                if (selected < 0) {
                    return false;
                }
                values[n + selected]--;
                // Remove the latest copy of this identity, retaining FIFO priority.
                for (int q = values.Length - 1; q >= 3 * n + 2; q--) {
                    if (values[q] == selected) {
                        if (!cards[selected].DrawOncePerTurn || values[n + selected] == 0) {
                            selectedQueue = q;
                        }
                        break;
                    }
                }
                if (selectedQueue >= 0) {
                    values = [.. values.Take(selectedQueue), .. values.Skip(selectedQueue + 1)];
                }
            }
            values[3 * n + 1] = 0;
            while (values.Length > 3 * n + 2) {
                budget.Spend(2L * values.Length + 1);
                int source = values[3 * n + 2];
                values = [.. values.Take(3 * n + 2), .. values.Skip(3 * n + 3)];
                if (values[2 * n + source] != 0) {
                    continue;
                }
                values[n + source]--;
                if (cards[source].DrawOncePerTurn) {
                    values[2 * n + source] = 1;
                    values = [.. values.Take(3 * n + 2), .. values.Skip(3 * n + 2).Where(entry => entry != source)];
                }
                values[3 * n] = cards[source].DrawCount!.Value;
                values[3 * n + 1] = cards[source].PostDrawDiscardCount;
                return values[3 * n] <= remaining;
            }
            return true;
        }

        void Add(
            Dictionary<State, BigInteger> map,
            State key,
            BigInteger increment,
            ref long cells
        ) {
            // Both lookup and insertion hash and may compare the complete vector.
            budget.Spend(4L * key.Values.Length + WorkBudget.IntegerCells(increment) + 1);
            bool exists = map.TryGetValue(key, out BigInteger previous);
            budget.Spend(WorkBudget.IntegerCells(previous));
            BigInteger value = previous + increment;
            cells += WorkBudget.IntegerCells(value) - WorkBudget.IntegerCells(previous) + (exists ? 0 : key.Values.Length);
            budget.CheckStorage(map.Count + (exists ? 0 : 1), cells);
            map[key] = value;
        }
    }

    private List<Card> ReduceDeck(List<Combo> combos) {
        // Canonical tie order: membership bit string for ordinal category identities,
        // then ordinal direct-card IDs (including maximum-only selectors), ordinary
        // before effect, then ordinal Card.Id, then earlier encounter within an ID.
        // Ordinary cards with equal signatures are indistinguishable to every
        // requirement and to the policy. Their counts can therefore be merged.
        long selectors = 0;
        foreach (Combo combo in combos) {
            selectors += combo.Categories.Count + (long)combo.Cards.Count;
            foreach (ComboAlternativeGroup group in combo.AlternativeGroups) {
                selectors += group.Alternatives.Count;
            }
            budget.CheckStorage(selectors, selectors);
            budget.Spend(combo.RequirementCount + 1L);
        }
        budget.CheckStorage(deck.Count, (selectors + 1) * deck.Count);
        budget.Spend(selectors * (selectors + 1) + deck.Count * (long)(deck.Count + 1) * (selectors + 1));
        CategoryBase[] categories = combos.SelectMany(combo => combo.AllCategories)
            .Select(requirement => requirement.BaseCategory).Distinct().OrderBy(category => category.Identity, StringComparer.Ordinal).ToArray();
        string[] direct = combos.SelectMany(combo => combo.AllCards).Select(requirement => requirement.CardId)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        List<(Card Card, string Signature)> entries = [];
        foreach (Card card in deck) {
            ArgumentOutOfRangeException.ThrowIfNegative(card.Copies);
            budget.Spend((categories.Length + 1L) * (card.Categories.Count + 1L) + direct.Length);
            budget.CheckStorage(deck.Count, (categories.Length + (long)direct.Length + 1) * deck.Count);
            string signature = string.Concat(categories.Select(category => card.Categories.Contains(category) ? '1' : '0'))
                + string.Concat(direct.Select(id => card.Id == id ? '1' : '0'));
            entries.Add((card, signature));
        }
        List<Card> reduced = [];
        foreach (IGrouping<string, (Card Card, string Signature)> group in entries.GroupBy(entry => entry.Signature).OrderBy(group => group.Key, StringComparer.Ordinal)) {
            Card[] ordinary = group.Where(entry => entry.Card.DrawCount is null).Select(entry => entry.Card)
                .OrderBy(card => card.Id, StringComparer.Ordinal).ToArray();
            if (ordinary.Length > 0) {
                reduced.Add(ordinary[0].WithCopies(ordinary.Sum(card => card.Copies)));
            }
            reduced.AddRange(group.Where(entry => entry.Card.DrawCount is not null).Select(entry => entry.Card)
                .OrderBy(card => card.Id, StringComparer.Ordinal));
        }
        return reduced;
    }
}
