using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

// A layer counts visible labeled prefixes of equal length. Hidden removals leave
// identities unobserved: their counts stay in the identity pool, while a separate
// total reduces physical capacity. Exchangeability makes each next visible label
// uniform in that pool. Unobserved suffixes contribute (N - depth)!, including
// hidden positions, so all outcomes share N! even when physical capacity is zero.
// No physical permutations are enumerated and no probability mass is rounded.
internal sealed partial class PostDrawCalculation(
    List<Card> deck,
    int handSize,
    WorkBudget budget,
    DrawActivationPriority? priority = null
) {
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

    private enum ResolutionOutcome { Ready, RemovalUnavailable }

    // Owned immutable vectors: remaining counts, hand counts, only actual OPT flags,
    // batch draws left, pending removal source (index + 1), optional hidden-card count,
    // then FIFO candidate entry indices. Hidden identities remain in the first vector.
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
        bool customPriority = priority?.Mode == DrawActivationMode.CustomPriority;
        if (customPriority) {
            budget.Spend(n + (long)priority!.CardIds.Count);
            budget.CheckStorage(n, n);
        }
        int[] ranks = customPriority ? cards.Select(card => priority!.Rank(card.Id)).ToArray() : [];
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
        bool hasDeckRemoval = cards.Any(card => card.PreDrawDeckRemovalCount > 0 || card.PostDrawDeckMillCount > 0);
        int hiddenSlot = phase + 2;
        int queueStart = phase + 2 + (hasDeckRemoval ? 1 : 0);
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
            long categoryAtoms = combo.AllCategories.Sum(requirement => (long)requirement.Selector.CategoryIdentities.Count);
            long deckMemberships = n + cards.Sum(card => (long)card.Categories.Count);
            budget.Spend((combo.AllCards.Count() + categoryAtoms + 1L) * deckMemberships);
        }
        bool[] relevant = cards.Select(card => combos.Any(combo =>
            combo.AllCards.Any(requirement => requirement.MinCount > 0 && requirement.CardId == card.Id)
            || combo.AllCategories.Any(requirement => requirement.MinCount > 0 && requirement.Selector.Matches(card.Categories)))).ToArray();

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
        // A single effect identity has no order dimension under either priority policy. For a hand-neutral,
        // irrelevant, non-OPT effect, relevant ordinary cards always retain the
        // best handSize copies seen so far. Keep this optimization only when all
        // possible batches fit; exhaustion can change both the final hand size and
        // the effect schedule, which the general resolver handles exactly.
        if (effects.Length == 1 && filler.Length <= 1) {
            int effect = effects[0];
            if (DrawExecutionAssessment.IsExchangeableEffect(cards[effect], relevant[effect], copies, handSize)) {
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

        // Keep post-draw-only decks free of cost tables.
        bool[]?[]? costEligibility = null;
        if (cards.Any(card => card.PreDrawRemovalCount > 0)) {
            eligibilityCells += n;
            CheckStorage(n, eligibilityCells, "removal selector table");
            costEligibility = new bool[n][];
            for (int i = 0; i < n; i++) {
                if (cards[i].PreDrawRemovalCount > 0 && cards[i].PreDrawSelector.Kind != CardSelectorKind.Any) {
                    eligibilityCells += n;
                    CheckStorage(n, eligibilityCells, "removal selector table");
                    costEligibility[i] = CardSelectorCompiler.Compile(cards[i].PreDrawSelector, cards, budget);
                }
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
                // Initial state is ready; every later state was settled before
                // insertion. Read its immutable vector directly. Only freshly
                // allocated child vectors may be changed by drawing or Settle.
                budget.Spend(1);
                int[] values = state.Values;
                int remaining = copies - depth;
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
                    if (Settle(ref drawn, remaining - 1) != ResolutionOutcome.RemovalUnavailable) {
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
            remaining -= hasDeckRemoval ? values[hiddenSlot] : 0;
            while (true) {
                if (values[phase] > remaining) {
                    throw new InvalidOperationException("An activated draw batch exceeds the remaining deck size.");
                }
                if (values[phase] > 0) {
                    return ResolutionOutcome.Ready;
                }

                int removalSource = values[phase + 1] - 1;
                // The batch has completed. Mill before hand removal and any waiting activation.
                if (removalSource >= 0 && cards[removalSource].PostDrawDeckMillCount > 0) {
                    int mill = cards[removalSource].PostDrawDeckMillCount;
                    values[hiddenSlot] += mill;
                    remaining -= mill;
                }
                int removalCount = removalSource < 0 ? 0 : cards[removalSource].PostDrawDiscardCount;
                bool[]? eligible = removalSource < 0 ? null : removalEligibility[removalSource];
                for (int discard = removalCount; discard > 0; discard--) {
                    if (!RemoveCard(ref values, eligible, remaining)) {
                        return cards[removalSource].PostDrawFailureBehavior switch {
                            PostDrawFailureBehavior.FailBranch => ResolutionOutcome.RemovalUnavailable,
                            _ => throw new ArgumentOutOfRangeException(nameof(cards))
                        };
                    }
                }
                values[phase + 1] = 0;

                int selectedCandidate = -1;
                int selectedRank = int.MaxValue;
                // Keep the physical FIFO intact, including for cost/removal choices. Custom
                // priority selects only among legal candidates; equal/unlisted ranks keep FIFO.
                // Deferred candidates retain FIFO position. Restart at the oldest
                // survivor after an activation's batch/removal. A pass with no
                // activation terminates: nothing changed that could help a retry.
                // Every successful pass consumes a source; draws advance depth.
                // This also bounds resolution when the deck is already empty.
                for (int candidate = queueStart; candidate < values.Length;) {
                    budget.Spend(2L * values.Length + 1);
                    int source = values[candidate];
                    if (Used(values, source)) {
                        values = RemoveCandidate(values, candidate);
                        continue;
                    }
                    if (cards[source].DrawCount!.Value + cards[source].PreDrawDeckRemovalCount + cards[source].PostDrawDeckMillCount > remaining) {
                        values = RemoveCandidate(values, candidate);
                        continue;
                    }
                    if (cards[source].PreDrawRemovalCount > 0 && !CanPay(values, source)) {
                        candidate++;
                        continue;
                    }
                    if (selectedCandidate < 0 || ranks[source] < selectedRank) {
                        selectedCandidate = candidate;
                        if (!customPriority) {
                            break;
                        }
                        selectedRank = ranks[source];
                    }
                    candidate++;
                }
                if (selectedCandidate >= 0) {
                    int source = values[selectedCandidate];
                    values = RemoveCandidate(values, selectedCandidate);
                    // Check the whole cost before mutating activation state, and
                    // reserve this source copy while selecting/removing material.
                    for (int cost = 0; cost < cards[source].PreDrawRemovalCount; cost++) {
                        if (!RemoveCard(ref values, costEligibility![source], remaining, source)) {
                            throw new InvalidOperationException("A validated activation cost became unavailable.");
                        }
                    }
                    if (hasDeckRemoval) {
                        values[hiddenSlot] += cards[source].PreDrawDeckRemovalCount;
                        remaining -= cards[source].PreDrawDeckRemovalCount;
                    }
                    values[n + source]--;
                    if (cards[source].DrawOncePerTurn) {
                        values[optSlots[source]] = 1;
                        // Enqueue admits at most one pending entry per OPT
                        // identity. Removing the activating candidate above
                        // already removed its only queue entry.
                    }
                    values[phase] = cards[source].DrawCount!.Value;
                    values[phase + 1] = cards[source].PostDrawDiscardCount > 0 || cards[source].PostDrawDeckMillCount > 0 ? source + 1 : 0;
                }
                else {
                    if (values.Length > queueStart) {
                        // No pending draw/removal can change this hand. Retain the
                        // cards, but canonicalize the terminal queue so the next
                        // frontier visit cannot recheck the same failed costs.
                        budget.Spend(values.Length);
                        values = values.AsSpan(0, queueStart).ToArray();
                    }
                    return ResolutionOutcome.Ready;
                }
            }
        }

        bool CanPay(int[] values, int source, int reservedSource = -1) {
            budget.Spend(n + 1L);
            bool[]? eligible = costEligibility![source];
            int available = 0;
            for (int i = 0; i < n; i++) {
                if (eligible is null || eligible[i]) {
                    available += values[n + i] - (i == source ? 1 : 0) - (i == reservedSource ? 1 : 0);
                }
            }
            return available >= cards[source].PreDrawRemovalCount;
        }

        bool RemoveCard(ref int[] values, bool[]? eligible, int remaining, int reservedSource = -1) {
            // Queue scan, activation classification, tier scan, removal scan and copying the surviving queue.
            budget.Spend(2L * values.Length + 2L * n + 1);
            int selected = -1;
            int worstTier = 0;
            int selectedQueue = -1;
            bool[] pending = new bool[n];
            for (int q = queueStart; q < values.Length; q++) {
                int entry = values[q];
                pending[entry] = true;
            }

            bool[] activatable = new bool[n];
            bool[] costDeferred = new bool[n];
            int minimumLegalDeckConsumption = int.MaxValue;
            if (reservedSource >= 0 && cards[reservedSource].DrawCount is int reservedDrawCount
                && reservedDrawCount + cards[reservedSource].PreDrawDeckRemovalCount + cards[reservedSource].PostDrawDeckMillCount <= remaining) {
                // A reserved source has already passed its activation checks and is finishing its cost.
                minimumLegalDeckConsumption = reservedDrawCount + cards[reservedSource].PreDrawDeckRemovalCount + cards[reservedSource].PostDrawDeckMillCount;
            }
            for (int i = 0; i < n; i++) {
                if (!pending[i] || values[n + i] - (i == reservedSource ? 1 : 0) == 0 || Used(values, i)
                    || cards[i].DrawCount is not int drawCount || drawCount + cards[i].PreDrawDeckRemovalCount + cards[i].PostDrawDeckMillCount > remaining
                    || (i == reservedSource && cards[i].DrawOncePerTurn)) {
                    continue;
                }

                bool canPay = cards[i].PreDrawRemovalCount == 0 || CanPay(values, i, reservedSource);
                if (canPay) {
                    activatable[i] = true;
                    minimumLegalDeckConsumption = Math.Min(minimumLegalDeckConsumption, drawCount + cards[i].PreDrawDeckRemovalCount + cards[i].PostDrawDeckMillCount);
                }
                else if (cards[i].PreDrawRemovalCount > 0) {
                    costDeferred[i] = true;
                }
            }

            // The smallest legal deck consumption leaves the most room for a deferred source.
            int remainingAfterAnotherDraw = minimumLegalDeckConsumption == int.MaxValue
                ? -1
                : remaining - minimumLegalDeckConsumption;
            for (int i = 0; i < n; i++) {
                if (values[n + i] - (i == reservedSource ? 1 : 0) == 0 || (eligible is not null && !eligible[i])) {
                    continue;
                }
                bool usable = activatable[i];
                bool redundant = cards[i].DrawOncePerTurn && values[n + i] - (i == reservedSource ? 1 : 0) > 1;
                bool deferred = costDeferred[i] && !redundant
                    && cards[i].DrawCount is int drawCount && drawCount + cards[i].PreDrawDeckRemovalCount + cards[i].PostDrawDeckMillCount <= remainingAfterAnotherDraw;
                int tier = (relevant[i], usable && !redundant, deferred) switch {
                    (true, _, _) => 1,
                    (_, true, _) => 2,
                    (_, _, true) => 3,
                    _ => 4
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
            for (int q = values.Length - 1; q >= queueStart; q--) {
                if (values[q] == selected) {
                    if (!cards[selected].DrawOncePerTurn || values[n + selected] == 0) {
                        selectedQueue = q;
                    }
                    break;
                }
            }
            if (selectedQueue >= 0) {
                values = RemoveCandidate(values, selectedQueue);
            }
            return true;
        }

        void Add(
            Dictionary<State, BigInteger> map,
            State key,
            BigInteger increment,
            ref long cells
        ) {
            // Existing states need one hash/comparison pass. A missing state
            // still pays for insertion, after checking the prospective storage.
            budget.Spend(2L * key.Values.Length + WorkBudget.IntegerCells(increment) + 1);
            ref BigInteger slot = ref CollectionsMarshal.GetValueRefOrNullRef(map, key);
            bool exists = !Unsafe.IsNullRef(ref slot);
            BigInteger previous = exists ? slot : BigInteger.Zero;
            budget.Spend(WorkBudget.IntegerCells(previous));
            BigInteger value = previous + increment;
            cells += WorkBudget.IntegerCells(value) - WorkBudget.IntegerCells(previous) + (exists ? 0 : key.Values.Length);
            int count = map.Count + (exists ? 0 : 1);
            PeakStates = Math.Max(PeakStates, count);
            CheckStorage(count, cells, ReferenceEquals(map, terminal) ? "terminal hands" : "draw frontier");
            if (exists) {
                // No map mutation between obtaining and using this reference.
                slot = value;
            }
            else {
                budget.Spend(2L * key.Values.Length);
                map.Add(key, value);
            }
        }
    }

    private static int[] RemoveCandidate(int[] values, int index) {
        int[] result = new int[values.Length - 1];
        values.AsSpan(0, index).CopyTo(result);
        values.AsSpan(index + 1).CopyTo(result.AsSpan(index));
        return result;
    }

    private List<Card> ReduceDeck(List<Combo> combos) {
        // Canonical tie order: membership bit string for ordinal category identities,
        // then ordinal direct-card IDs (including maximum-only selectors), ordinary
        // before effect, then ordinal Card.Id, then earlier encounter within an ID.
        // Ordinary cards with equal signatures are indistinguishable to every
        // requirement and to the policy. Their counts can therefore be merged.
        long selectors = 0;
        foreach (Combo combo in combos) {
            selectors += combo.Categories.Sum(requirement => (long)requirement.Selector.CategoryIdentities.Count) + combo.Cards.Count;
            foreach (ComboAlternativeGroup group in combo.AlternativeGroups) {
                selectors += group.Alternatives.Sum(alternative => alternative.Category is { } category
                    ? (long)category.Selector.CategoryIdentities.Count : 1L);
            }
            CheckStorage(selectors, selectors, "policy selectors");
            budget.Spend(combo.RequirementCount + 1L);
        }
        CheckStorage(deck.Count, (selectors + 1) * deck.Count, "policy descriptors");
        budget.Spend(selectors * (selectors + 1) + deck.Count * (long)(deck.Count + 1) * (selectors + 1));
        CategoryBase[] categories = combos.SelectMany(combo => combo.AllCategories)
            .SelectMany(requirement => requirement.CategoryBases).Distinct().OrderBy(category => category.Identity, StringComparer.Ordinal).ToArray();
        string[] direct = combos.SelectMany(combo => combo.AllCards).Select(requirement => requirement.CardId)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        List<bool[]> filters = [];
        foreach (Card source in deck) {
            if (source.Copies > 0 && source.PostDrawDiscardCount > 0 && source.PostDrawSelector.Kind != CardSelectorKind.Any) {
                CheckStorage(filters.Count + 1L, (filters.Count + 1L) * deck.Count, "reduction selectors");
                filters.Add(CardSelectorCompiler.Compile(source.PostDrawSelector, deck, budget));
            }
        }
        foreach (Card source in deck) {
            if (source.Copies > 0 && source.PreDrawRemovalCount > 0 && source.PreDrawSelector.Kind != CardSelectorKind.Any) {
                CheckStorage(filters.Count + 1L, (filters.Count + 1L) * deck.Count, "reduction selectors");
                filters.Add(CardSelectorCompiler.Compile(source.PreDrawSelector, deck, budget));
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
            // Effect entries stay separate, preserving OPT identity AND custom rank.
            reduced.AddRange(group.Where(entry => entry.Card.DrawCount is not null).Select(entry => entry.Card)
                .OrderBy(card => card.Id, StringComparer.Ordinal));
        }
        return reduced;
    }
}
