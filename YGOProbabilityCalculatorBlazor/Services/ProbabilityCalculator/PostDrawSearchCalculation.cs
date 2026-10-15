using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

internal sealed partial class PostDrawCalculation {
    private interface ITerminalHand {
        static abstract int[] Create(int[] state, OrderedDrawStateLayout layout, int remaining);
    }

    private readonly struct HandTerminal : ITerminalHand {
        public static int[] Create(int[] state, OrderedDrawStateLayout layout, int remaining) =>
            state.AsSpan(layout.HandStart, layout.RowCount).ToArray();
    }

    private readonly struct SearchTerminal : ITerminalHand {
        public static int[] Create(int[] state, OrderedDrawStateLayout layout, int remaining) {
            int[] hand = new int[layout.RowCount + 1];
            state.AsSpan(layout.HandStart, layout.RowCount).CopyTo(hand);
            hand[^1] = Math.Max(0, remaining - (layout.HasHiddenCount ? state[layout.HiddenCount] : 0));

            return hand;
        }
    }

    internal ExactCalculationResult CalculateSearch(List<Combo> combos, IReadOnlyList<ComboGroup>? groups, bool totalOnly, SearchAccessPlan search) {
        List<Card> cards = ReduceSearchDeck(combos, out Dictionary<string, List<Card>> identities);
        // Only removable searchers need policy target counts. Unrelated targets
        // can still join the deferred pool when no removal can observe them.
        HashSet<string> observed = SearchPolicyObservedRows(cards);
        List<Card> deferred = deferIdentities ? DeferUnobservedIdentities(ref cards, observed) : [];
        int n = cards.Count;
        bool customPriority = priority?.Mode == DrawActivationMode.CustomPriority;
        if (customPriority) {
            budget.Spend(n + (long)priority!.CardIds.Count);
            budget.CheckStorage(n, n);
        }
        int[] ranks = customPriority ? cards.Select(card => priority!.Rank(card.Id)).ToArray() : [];
        ReducedRows = n;
        OrderedDrawStateLayout layout = new(cards);
        int copies = 0;
        foreach (Card card in cards) {
            budget.Spend(1);
            ArgumentOutOfRangeException.ThrowIfNegative(card.Copies);
            copies = checked(copies + card.Copies);
        }
        if (handSize < 0 || handSize > copies) {
            throw new ArgumentOutOfRangeException(nameof(handSize));
        }

        CheckStorage(1, layout.QueueStart + (long)copies + 1, "policy descriptors");
        foreach (Combo combo in combos) {
            long categoryAtoms = combo.AllCategories.Sum(requirement => (long)requirement.Selector.CategoryIdentities.Count);
            long deckMemberships = n + cards.Sum(card => (long)card.Categories.Count);
            budget.Spend((combo.AllCards.Count() + categoryAtoms + 1L) * deckMemberships);
        }
        bool[] relevant = cards.Select(card => combos.Any(combo =>
            combo.AllCards.Any(requirement => requirement.MinCount > 0 && requirement.CardId == card.Id)
            || combo.AllCategories.Any(requirement => requirement.MinCount > 0 && requirement.Selector.Matches(card)))).ToArray();

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

        OrderedResolver resolver = new(
            this,
            budget,
            cards,
            layout,
            customPriority,
            ranks,
            relevant,
            removalEligibility,
            costEligibility,
            new SearchKeepPolicy(cards, relevant, budget)
        );
        Dictionary<State, BigInteger> terminal = resolver.Resolve<SearchTerminal>(handSize, copies, factorial, compactFrontierThreshold);
        TerminalStates = terminal.Count;

        List<List<Card>> pools = cards.Select(card => identities[card.Id]).ToList();
        if (deferred.Count > 0) {
            pools[^1] = deferred.SelectMany(card => identities[card.Id]).ToList();
        }

        return CountSearchHands(cards, pools, terminal, factorial[copies], combos, groups, totalOnly, search);
    }

    private List<Card> ReduceSearchDeck(List<Combo> combos, out Dictionary<string, List<Card>> identities) {
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
        // Equal combo/removal signatures alone do not imply search equivalence.
        // Keep target membership for every original source in each ordinary run.
        foreach (Card source in deck.Where(card => card.Copies > 0 && card.Search is not null)) {
            CheckStorage(filters.Count + 1L, (filters.Count + 1L) * deck.Count, "search reduction selectors");
            budget.Spend(deck.Sum(card => card.Categories.Count + 1L) * (source.Search!.Selector.EvaluationSize + 1L));
            filters.Add(deck.Select(card => card.Id != source.Id && source.Search!.Matches(card)).ToArray());
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
        identities = new(StringComparer.Ordinal);
        foreach (IGrouping<string, (Card Card, string Signature, string Eligibility)> group in entries
            .GroupBy(entry => entry.Signature).OrderBy(group => group.Key, StringComparer.Ordinal)) {
            // Retain the established membership/ordinary/ID tie order. Only merge
            // adjacent ordinary runs with equal eligibility for every effect.
            // Combining interleaved IDs across different filters changes removals.
            string? previousEligibility = null;
            foreach ((Card card, string _, string eligibility) in group.Where(entry => entry.Card.DrawCount is null)
                .OrderBy(entry => entry.Card.Id, StringComparer.Ordinal)) {
                if (card.Search is null && reduced.Count > 0 && reduced[^1].Search is null && eligibility == previousEligibility) {
                    identities[reduced[^1].Id].Add(card);
                    reduced[^1] = reduced[^1].WithCopies(checked(reduced[^1].Copies + card.Copies));
                }
                else {
                    reduced.Add(card);
                    identities.Add(card.Id, [card]);
                    previousEligibility = eligibility;
                }
            }
            // Effect entries stay separate, preserving OPT identity AND custom rank.
            foreach (Card card in group.Where(entry => entry.Card.DrawCount is not null).Select(entry => entry.Card)
                .OrderBy(card => card.Id, StringComparer.Ordinal)) {
                reduced.Add(card);
                identities.Add(card.Id, [card]);
            }
        }
        return reduced;
    }

    // Coalesced ordinary labels share every combo, search and removal mask;
    // their individual counts cannot change any final access event. Averaging
    // those labels is harmless even when ID ties bias their physical survival.
    // Deferred labels have a stronger proof: swapping them leaves the complete
    // trace unchanged (including search retention), so their subsets are uniform.
    // Neither case reconstructs the identities of remaining deck cards.
    private ExactCalculationResult CountSearchHands(
        List<Card> cards,
        List<List<Card>> pools,
        Dictionary<State, BigInteger> terminal,
        BigInteger denominator,
        List<Combo> combos,
        IReadOnlyList<ComboGroup>? groups,
        bool totalOnly,
        SearchAccessPlan search
    ) {
        BigInteger[] wins = new BigInteger[1 + (totalOnly ? 0 : combos.Count + (groups?.Count ?? 0))];
        CheckStorage(wins.Length, wins.Length * (WorkBudget.IntegerCells(denominator) + 1), "result accumulators");
        foreach ((State hand, BigInteger mass) in terminal) {
            List<Card> retained = [];
            List<SearchHandPool> random = [];
            for (int row = 0; row < cards.Count; row++) {
                budget.Spend(1);
                if (pools[row].Count == 1) {
                    retained.Add(pools[row][0].WithCopies(hand.Values[row]));
                }
                else {
                    random.Add(new(pools[row], hand.Values[row]));
                }
            }
            SearchProbabilityCalculation calculation = new(random.SelectMany(pool => pool.Cards).ToList(),
                random.Sum(pool => pool.Held), retained, search, hand.Values[^1], budget, random);
            ExactCalculationResult result = calculation.Calculate(combos, groups, totalOnly);
            ExactProbability[] values = [result.Total, .. result.Combos, .. result.Groups];
            for (int i = 0; i < values.Length; i++) {
                ExactProbability probability = values[i];
                budget.Spend((WorkBudget.IntegerCells(mass) + probability.Cells + 1) * (probability.Cells + 1));
                BigInteger contribution = BigInteger.DivRem(mass * probability.Numerator, probability.Denominator, out BigInteger remainder);
                if (!remainder.IsZero) {
                    throw new InvalidOperationException("Conditional pool subsets must have equal integer permutation mass.");
                }
                wins[i] += contribution;
            }
        }
        ExactProbability[] results = wins.Select(win => new ExactProbability(win, denominator)).ToArray();

        return new(results[0], totalOnly ? [] : results.Skip(1).Take(combos.Count).ToArray(),
            totalOnly ? [] : results.Skip(1 + combos.Count).ToArray());
    }
}
