using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

// New predicate: expand OR choices, check every maximum against the whole hand,
// then backtrack over distinct physical slots for positive requirements.
// No production compiler, Hall subsets, decision diagram, or old search oracle.
internal static class SearchCertificateHandOracle {
    internal static bool MatchesHand(IReadOnlyList<Card> hand, Combo combo) {
        List<(Func<Card, bool> Match, int Min, int Max)> required = [];
        foreach (ComboCategory category in combo.Categories) { required.Add(Category(category)); }
        foreach (ComboCard card in combo.Cards) { required.Add(Direct(card)); }
        return Alternatives(0);

        (Func<Card, bool>, int, int) Category(ComboCategory c) =>
            (card => c.CategoryBases.All(b => card.Categories.Any(x => x.Identity == b.Identity)), c.MinCount,
                c.MaximumMode == RequirementMaximumMode.HandSize ? hand.Count : c.MaxCount);
        (Func<Card, bool>, int, int) Direct(ComboCard c) =>
            (card => card.Id == c.CardId, c.MinCount, c.MaximumMode == RequirementMaximumMode.HandSize ? hand.Count : c.MaxCount);
        bool Alternatives(int group) {
            if (group < combo.AlternativeGroups.Count) {
                foreach (ComboAlternative alternative in combo.AlternativeGroups[group].Alternatives) {
                    required.Add(alternative.Category is { } c ? Category(c) : Direct(alternative.Card!));
                    bool found = Alternatives(group + 1);
                    required.RemoveAt(required.Count - 1);
                    if (found) { return true; }
                }
                return false;
            }
            List<Func<Card, bool>> slots = [];
            foreach ((Func<Card, bool> match, int min, int max) in required) {
                int count = hand.Count(match);
                if (count < min || count > max) { return false; }
                for (int i = 0; i < min; i++) { slots.Add(match); }
            }
            bool[] used = new bool[hand.Count];
            return Assign(0);
            bool Assign(int role) {
                if (role == slots.Count) { return true; }
                for (int i = 0; i < hand.Count; i++) {
                    if (!used[i] && slots[role](hand[i])) {
                        used[i] = true;
                        if (Assign(role + 1)) { return true; }
                        used[i] = false;
                    }
                }
                return false;
            }
        }
    }
}

internal static class SearchCertificateOracle {
    internal static long SearchCalls;
    internal static long LossAllocations;
    internal sealed record Reach(bool[] Results, HashSet<string> Hands);
    internal sealed record Counts(BigInteger Outcomes, BigInteger Failed, BigInteger[][] Wins, long ResolvedStates, long SearchCalls, long LossAllocations);

    // Independent selector implementation, including objective/manual provenance.
    internal static bool Eligible(SearchEffectPlan search, Card card) {
        if (!card.Active || card.Copies <= 0 || (search.ObjectiveMetadataOnly && card.ExternalCardId is not > 0)) { return false; }
        HashSet<string> keys = card.Categories.Where(c => !search.ObjectiveMetadataOnly ||
            (c.Source == CategorySource.Metadata && !card.ManualMetadataCategoryKeys.Contains(c.MetadataKey!)))
            .Select(c => c.Identity).ToHashSet(StringComparer.Ordinal);
        return Match(search.Selector);
        bool Match(CardSelector selector) {
            if (selector.Kind == CardSelectorKind.Any) { return true; }
            if (selector.Kind == CardSelectorKind.LevelAtLeast) {
                string[] metadata = card.Categories.Where(category => category.Source == CategorySource.Metadata
                    && !card.ManualMetadataCategoryKeys.Contains(category.MetadataKey!)).Select(category => category.MetadataKey!).ToArray();
                string[] levels = metadata.Where(key => key.StartsWith("level:", StringComparison.Ordinal)).ToArray();
                bool excluded = metadata.Any(key => key.StartsWith("rank:", StringComparison.Ordinal)
                    || key.StartsWith("link:", StringComparison.Ordinal) || key is "monster-type:xyz" or "monster-type:link");
                return card.ExternalCardId > 0 && metadata.Contains("kind:monster") && !excluded && levels.Length == 1
                    && int.TryParse(levels[0][6..], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int level)
                    && level >= selector.MinimumLevel;
            }
            if (selector.Children.Count > 0) {
                return selector.Kind == CardSelectorKind.AllOf ? selector.Children.All(Match) : selector.Children.Any(Match);
            }
            return selector.Kind == CardSelectorKind.AllOf ? selector.CategoryIdentities.All(keys.Contains)
                : selector.CategoryIdentities.Any(keys.Contains);
        }
    }

    internal static Reach Search(List<Card> deck, IReadOnlyList<Card> originalHand, int[] capacities, int remaining,
        List<Combo> combos, List<ComboGroup> groups, bool recordHands = true) {
        SearchCalls++;
        Card[] hand = originalHand.ToArray();
        int[] available = (int[])capacities.Clone();
        HashSet<string> used = new(StringComparer.Ordinal);
        bool[] matches = new bool[combos.Count];
        HashSet<string> hands = new(StringComparer.Ordinal);
        Visit(0, remaining);
        return new([matches.Any(x => x), .. matches, .. groups.Select(g => combos.Where((c, i) => c.GroupId == g.Id && matches[i]).Any())], hands);
        void Visit(int slot, int left) {
            if (slot == hand.Length) {
                if (recordHands) { hands.Add(string.Join(",", deck.Select(c => hand.Count(h => h.Id == c.Id)))); }
                for (int c = 0; c < combos.Count; c++) {
                    if (!matches[c] && SearchCertificateHandOracle.MatchesHand(hand, combos[c])) { matches[c] = true; }
                }
                return;
            }
            Visit(slot + 1, left); // optional; includes the empty assignment
            Card source = originalHand[slot]; // fetched sources never activate
            if (left == 0 || source.Search is null || (source.OncePerTurn && used.Contains(source.Id))) { return; }
            for (int target = 0; target < deck.Count; target++) {
                if (available[target] <= 0 || deck[target].Id == source.Id || !Eligible(source.Search, deck[target])) { continue; }
                bool added = used.Add(source.Id);
                available[target]--;
                hand[slot] = deck[target];
                Visit(slot + 1, left - 1);
                hand[slot] = source;
                available[target]++;
                if (added) { used.Remove(source.Id); }
            }
        }
    }

    internal static Counts Enumerate(List<Card> input, int size, List<Combo> combos, List<ComboGroup> groups, string[]? priority = null,
        Action<List<Card>, int, int, bool[]>? checkTerminal = null, bool collapseIdenticalCopies = true) {
        List<Card> deck = input.Where(c => c.Active && c.Copies > 0).ToList();
        Card[] labels = deck.SelectMany(c => Enumerable.Repeat(c, c.Copies)).ToArray();
        int[] order = Enumerable.Range(0, labels.Length).ToArray();
        // Copies of one object are indistinguishable at EVERY deck position,
        // including hidden losses and encounter-order ties. Enumerate each such
        // sequence once with its exact labeled multiplicity. Distinct entries
        // remain distinct even when their names, selectors or memberships match.
        BigInteger multiplicity = 1;
        if (collapseIdenticalCopies) {
            foreach (IGrouping<Card, Card> copies in labels.GroupBy<Card, Card>(card => card, ReferenceEqualityComparer.Instance)) {
                for (int copy = 2; copy <= copies.Count(); copy++) { multiplicity *= copy; }
            }
        }
        bool repeatedCopies = multiplicity > BigInteger.One;
        BigInteger total = 0;
        BigInteger failed = 0;
        // lower elementary, physical, optimistic, robust shared-loss lower, no-search
        BigInteger[][] wins = Enumerable.Range(0, 5).Select(_ => new BigInteger[1 + combos.Count + groups.Count]).ToArray();
        Dictionary<string, bool[][]> cache = new(StringComparer.Ordinal);
        long startCalls = SearchCalls;
        long startLosses = LossAllocations;
        Permute(0);
        return new(total, failed, wins, cache.Count, SearchCalls - startCalls, LossAllocations - startLosses);

        void Permute(int position) {
            if (position == order.Length) { Evaluate(); return; }
            HashSet<Card>? seen = repeatedCopies ? new(ReferenceEqualityComparer.Instance) : null;
            for (int i = position; i < order.Length; i++) {
                if (seen is not null && !seen.Add(labels[order[i]])) {
                    continue;
                }
                (order[i], order[position]) = (order[position], order[i]);
                Permute(position + 1);
                (order[i], order[position]) = (order[position], order[i]);
            }
        }
        void Evaluate() {
            total += multiplicity;
            IReadOnlyList<Card> rest = [];
            List<Card>? hand = DrawSequenceOracle.Resolve(order.Select(i => labels[i]).ToArray(), size, combos, priority, cards => rest = cards);
            if (hand is null) { failed += multiplicity; return; }
            int[] held = deck.Select(c => hand.Count(h => h.Id == c.Id)).ToArray();
            int[] physical = deck.Select(c => rest.Count(h => h.Id == c.Id)).ToArray();
            string key = string.Join(',', held) + "|" + string.Join(',', physical);
            if (!cache.TryGetValue(key, out bool[][]? results)) {
                int d = labels.Length - hand.Count - rest.Count;
                int[] u = deck.Select((c, i) => c.Copies - held[i]).ToArray();
                int[] g = u.Select(n => Math.Max(0, n - d)).ToArray();
                for (int i = 0; i < deck.Count; i++) {
                    if (g[i] > physical[i] || physical[i] > u[i]) { throw new Exception("Capacity inequality: " + key); }
                }
                Reach lower = Search(deck, hand, g, rest.Count, combos, groups);
                checkTerminal?.Invoke(hand, rest.Count, d, lower.Results);
                Reach middle = Search(deck, hand, physical, rest.Count, combos, groups);
                Reach upper = Search(deck, hand, u, rest.Count, combos, groups);
                if (!lower.Hands.IsSubsetOf(middle.Hands) || !middle.Hands.IsSubsetOf(upper.Hands)) { throw new Exception("Pointwise inclusion: " + key); }
                bool[] robust = Enumerable.Repeat(true, upper.Results.Length).ToArray();
                int[] survivors = new int[deck.Count];
                Allocate(0, rest.Count);
                bool[] none = Search(deck, hand, new int[deck.Count], 0, combos, groups).Results;
                results = [lower.Results, middle.Results, upper.Results, robust, none];
                for (int i = 0; i < robust.Length; i++) {
                    if ((lower.Results[i] && !robust[i]) || (robust[i] && !middle.Results[i])) { throw new Exception("Robust inequality: " + key); }
                }
                cache.Add(key, results);
                void Allocate(int row, int left) {
                    if (row == survivors.Length) {
                        if (left != 0) { return; }
                        LossAllocations++;
                        bool[] success = Search(deck, hand, survivors, rest.Count, combos, groups, false).Results;
                        for (int i = 0; i < robust.Length; i++) { robust[i] &= success[i]; }
                        return;
                    }
                    for (int count = 0; count <= Math.Min(left, u[row]); count++) {
                        survivors[row] = count;
                        Allocate(row + 1, left - count);
                    }
                }
            }
            for (int mode = 0; mode < wins.Length; mode++) {
                for (int i = 0; i < wins[mode].Length; i++) { if (results[mode][i]) { wins[mode][i] += multiplicity; } }
            }
        }
    }
}
