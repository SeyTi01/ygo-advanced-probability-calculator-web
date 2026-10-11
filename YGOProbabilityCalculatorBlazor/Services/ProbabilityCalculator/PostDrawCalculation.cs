using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

// A layer counts labeled prefixes of equal length. Remaining multiplicities weight
// each next draw. Unseen suffixes contribute (N - depth)!, so all outcomes share N!.
// No physical permutations are enumerated and no probability mass is rounded.
internal sealed partial class PostDrawCalculation(List<Card> deck, int handSize, WorkBudget budget) {
    internal int TerminalStates { get; private set; }
    internal int PeakStates { get; private set; }
    internal long PeakCells { get; private set; }
    internal string? LargestStructure { get; private set; }
    internal int ReducedRows { get; private set; }

    private void CheckStorage(long entries, long cells, string structure) {
        if (cells > PeakCells) {
            PeakCells = cells;
            LargestStructure = structure;
        }
        budget.CheckStorage(entries, cells);
    }

    private enum ResolutionOutcome { Ready, DeckExhausted, RemovalUnavailable }

    // Owned immutable vectors: remaining counts, hand counts, only actual OPT flags,
    // batch draws left, pending removal source (index + 1), then FIFO candidate entry indices.
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
        ReducedRows = n;
        int[] optSlots = new int[n];
        Array.Fill(optSlots, -1);
        int optCount = 0;
        for (int i = 0; i < n; i++) {
            if (cards[i].DrawCount is not null && cards[i].DrawOncePerTurn) {
                optSlots[i] = 2 * n + optCount++;
            }
        }
        int phase = 2 * n + optCount;
        int queueStart = phase + 2;
        int copies = 0;
        foreach (Card card in cards) {
            budget.Spend(1);
            ArgumentOutOfRangeException.ThrowIfNegative(card.Copies);
            copies = checked(copies + card.Copies);
        }
        if (handSize < 0 || handSize > copies) {
            throw new ArgumentOutOfRangeException(nameof(handSize));
        }

        CheckStorage(1, queueStart + (long)copies + 1, "policy descriptors");
        foreach (Combo combo in combos) {
            budget.Spend((combo.AllCards.Count() + combo.AllCategories.Count() + 1L) * (n + cards.Sum(card => (long)card.Categories.Count)));
        }
        bool[] relevant = cards.Select(card => combos.Any(combo =>
            combo.AllCards.Any(requirement => requirement.MinCount > 0 && requirement.CardId == card.Id)
            || combo.AllCategories.Any(requirement => requirement.MinCount > 0 && card.Categories.Contains(requirement.BaseCategory)))).ToArray();

        CheckStorage(copies + 1L, copies + 1L, "factorials");
        BigInteger[] factorial = new BigInteger[copies + 1];
        factorial[0] = 1;
        long factorialCells = 1;
        for (int i = 1; i <= copies; i++) {
            budget.Spend(WorkBudget.IntegerCells(factorial[i - 1]) + 1);
            factorial[i] = factorial[i - 1] * i;
            factorialCells += WorkBudget.IntegerCells(factorial[i]);
            CheckStorage(copies + 1L, factorialCells, "factorials");
        }

        int[] effects = Enumerable.Range(0, n).Where(index => cards[index].DrawCount is not null).ToArray();
        int[] filler = Enumerable.Range(0, n).Where(index => !relevant[index] && cards[index].DrawCount is null).ToArray();
        // A single homogeneous FIFO has no order dimension. For a hand-neutral,
        // irrelevant, non-OPT effect, relevant ordinary cards always retain the
        // best handSize copies seen so far. Their identities cannot affect when
        // the effect resolves, so count that schedule separately from identities.
        if (effects.Length == 1 && filler.Length <= 1) {
            int effect = effects[0];
            if (!relevant[effect] && !cards[effect].DrawOncePerTurn
                && cards[effect].DrawCount == cards[effect].PostDrawDiscardCount + 1
                && cards[effect].PostDrawSelector.Kind == CardSelectorKind.Any) {
                return CalculateExchangeable(cards, relevant, effect, filler.FirstOrDefault(-1), factorial, combos, groups, totalOnly);
            }
        }

        CheckStorage(n, n, "removal selector table");
        bool[]?[] removalEligibility = new bool[n][];
        long eligibilityCells = n;
        for (int i = 0; i < n; i++) {
            if (cards[i].PostDrawDiscardCount > 0 && cards[i].PostDrawSelector.Kind != CardSelectorKind.Any) {
                eligibilityCells += n;
                CheckStorage(n, eligibilityCells, "removal selector table");
                removalEligibility[i] = CardSelectorCompiler.Compile(cards[i].PostDrawSelector, cards, budget);
            }
        }

        int[] initial = new int[queueStart];
        for (int i = 0; i < n; i++) {
            initial[i] = cards[i].Copies;
        }
        initial[phase] = handSize;
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
                if (Settle(ref values, remaining) != ResolutionOutcome.Ready) {
                    continue;
                }
                if (values[phase] == 0) {
                    budget.Spend(WorkBudget.IntegerCells(weight) * (WorkBudget.IntegerCells(factorial[remaining]) + 1));
                    Add(terminal, new(values.AsSpan(n, n).ToArray()), weight * factorial[remaining], ref terminalCells);
                    continue;
                }
                for (int i = 0; i < n; i++) {
                    budget.Spend(1);
                    if (values[i] == 0) {
                        continue;
                    }
                    bool candidate = cards[i].DrawCount is not null && !Used(values, i)
                        && (!cards[i].DrawOncePerTurn || !values.AsSpan(queueStart).Contains(i));
                    budget.Spend(values.Length + WorkBudget.IntegerCells(weight) + 1L);
                    int[] drawn = new int[values.Length + (candidate ? 1 : 0)];
                    values.CopyTo(drawn, 0);
                    drawn[i]--;
                    drawn[n + i]++;
                    drawn[phase]--;
                    if (candidate) {
                        drawn[^1] = i;
                    }
                    // Normalize immediately so batches differing only in discarded
                    // cards or obsolete candidates merge before the next layer.
                    if (Settle(ref drawn, remaining - 1) == ResolutionOutcome.Ready) {
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
        CheckStorage(resultCount, resultCount * (resultIntegerCells + 1), "result accumulators");
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

        bool Used(int[] values, int entry) => optSlots[entry] >= 0 && values[optSlots[entry]] != 0;

        ResolutionOutcome Settle(ref int[] values, int remaining) {
            if (values[phase] > 0) {
                return values[phase] <= remaining ? ResolutionOutcome.Ready : ResolutionOutcome.DeckExhausted;
            }
            int removalSource = values[phase + 1] - 1;
            int removalCount = removalSource < 0 ? 0 : cards[removalSource].PostDrawDiscardCount;
            bool[]? eligible = removalSource < 0 ? null : removalEligibility[removalSource];
            for (int discard = removalCount; discard > 0; discard--) {
                // Queue scan, tier scan, removal scan and copying the surviving queue.
                budget.Spend(2L * values.Length + n + 1);
                int selected = -1;
                int worstTier = 0;
                int selectedQueue = -1;
                bool[] pending = new bool[n];
                for (int q = queueStart; q < values.Length; q++) {
                    int entry = values[q];
                    pending[entry] = true;
                }
                for (int i = 0; i < n; i++) {
                    if (values[n + i] == 0 || (eligible is not null && !eligible[i])) {
                        continue;
                    }
                    bool usable = pending[i] && !Used(values, i) && cards[i].DrawCount <= remaining;
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
                    // Keep this separate from incomplete batch draws: changing
                    // deck exhaustion must not turn missing material into success.
                    return cards[removalSource].PostDrawFailureBehavior switch {
                        PostDrawFailureBehavior.FailBranch => ResolutionOutcome.RemovalUnavailable,
                        _ => throw new ArgumentOutOfRangeException(nameof(cards))
                    };
                }
                values[n + selected]--;
                // Remove the latest copy of this identity, retaining FIFO priority.
                for (int q = values.Length - 1; q >= queueStart; q--) {
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
            values[phase + 1] = 0;
            while (values.Length > queueStart) {
                budget.Spend(2L * values.Length + 1);
                int source = values[queueStart];
                values = [.. values.Take(queueStart), .. values.Skip(queueStart + 1)];
                if (Used(values, source)) {
                    continue;
                }
                values[n + source]--;
                if (cards[source].DrawOncePerTurn) {
                    values[optSlots[source]] = 1;
                    values = [.. values.Take(queueStart), .. values.Skip(queueStart).Where(entry => entry != source)];
                }
                values[phase] = cards[source].DrawCount!.Value;
                values[phase + 1] = cards[source].PostDrawDiscardCount > 0 ? source + 1 : 0;
                return values[phase] <= remaining ? ResolutionOutcome.Ready : ResolutionOutcome.DeckExhausted;
            }
            return ResolutionOutcome.Ready;
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
            int count = map.Count + (exists ? 0 : 1);
            PeakStates = Math.Max(PeakStates, count);
            CheckStorage(count, cells, ReferenceEquals(map, terminal) ? "terminal hands" : "draw frontier");
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
            CheckStorage(selectors, selectors, "policy selectors");
            budget.Spend(combo.RequirementCount + 1L);
        }
        CheckStorage(deck.Count, (selectors + 1) * deck.Count, "policy descriptors");
        budget.Spend(selectors * (selectors + 1) + deck.Count * (long)(deck.Count + 1) * (selectors + 1));
        CategoryBase[] categories = combos.SelectMany(combo => combo.AllCategories)
            .Select(requirement => requirement.BaseCategory).Distinct().OrderBy(category => category.Identity, StringComparer.Ordinal).ToArray();
        string[] direct = combos.SelectMany(combo => combo.AllCards).Select(requirement => requirement.CardId)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        List<bool[]> filters = [];
        foreach (Card source in deck) {
            if (source.Copies > 0 && source.PostDrawDiscardCount > 0 && source.PostDrawSelector.Kind != CardSelectorKind.Any) {
                CheckStorage(filters.Count + 1L, (filters.Count + 1L) * deck.Count, "reduction selectors");
                filters.Add(CardSelectorCompiler.Compile(source.PostDrawSelector, deck, budget));
            }
        }
        List<(Card Card, string Signature, string Eligibility)> entries = [];
        int row = 0;
        foreach (Card card in deck) {
            ArgumentOutOfRangeException.ThrowIfNegative(card.Copies);
            budget.Spend((categories.Length + 1L) * (card.Categories.Count + 1L) + direct.Length);
            CheckStorage(deck.Count, (categories.Length + (long)direct.Length + 1) * deck.Count, "policy descriptors");
            string signature = string.Concat(categories.Select(category => card.Categories.Contains(category) ? '1' : '0'))
                + string.Concat(direct.Select(id => card.Id == id ? '1' : '0'));
            budget.Spend(filters.Count);
            CheckStorage(deck.Count, (categories.Length + (long)direct.Length + filters.Count + 1) * deck.Count, "policy descriptors");
            entries.Add((card, signature, string.Concat(filters.Select(filter => filter[row] ? '1' : '0'))));
            row++;
        }
        List<Card> reduced = [];
        foreach (IGrouping<string, (Card Card, string Signature, string Eligibility)> group in entries
            .GroupBy(entry => entry.Signature).OrderBy(group => group.Key, StringComparer.Ordinal)) {
            // Retain the established membership/ordinary/ID tie order. Only merge
            // adjacent ordinary runs with equal eligibility for every effect.
            // Combining interleaved IDs across different filters changes removals.
            string? previousEligibility = null;
            foreach ((Card card, string _, string eligibility) in group.Where(entry => entry.Card.DrawCount is null)
                .OrderBy(entry => entry.Card.Id, StringComparer.Ordinal)) {
                if (eligibility == previousEligibility) {
                    reduced[^1] = reduced[^1].WithCopies(checked(reduced[^1].Copies + card.Copies));
                }
                else {
                    reduced.Add(card);
                    previousEligibility = eligibility;
                }
            }
            reduced.AddRange(group.Where(entry => entry.Card.DrawCount is not null).Select(entry => entry.Card)
                .OrderBy(card => card.Id, StringComparer.Ordinal));
        }
        return reduced;
    }
}
