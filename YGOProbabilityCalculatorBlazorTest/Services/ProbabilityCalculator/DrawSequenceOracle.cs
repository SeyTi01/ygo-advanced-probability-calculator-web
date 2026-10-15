using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

// Enumerate labeled physical permutations, deal the opener, then resolve queued
// effects in batches. No production scenario, mask, Hall or fraction helpers.
internal static class DrawSequenceOracle {
    // Physical labels distinguish copies; encounter order breaks ties within an ID.
    internal static List<Card>? Resolve(IReadOnlyList<Card> order, int handSize, IReadOnlyList<Combo> combos, IReadOnlyList<string>? priorityIds = null, Action<IReadOnlyList<Card>>? remainingDeck = null) {
        List<int> hand = Enumerable.Range(0, handSize).ToList();
        List<int> pending = hand.Where(index => order[index].DrawCount is not null).ToList();
        HashSet<string> used = new(StringComparer.Ordinal);
        int next = handSize;
        int activations = 0;
        bool locked = false;
        string[] categoryKeys = combos.SelectMany(combo => combo.AllCategories).SelectMany(requirement => requirement.CategoryBases.Select(category => category.Identity))
            .Distinct().Order(StringComparer.Ordinal).ToArray();
        string[] cardKeys = combos.SelectMany(combo => combo.AllCards).Select(requirement => requirement.CardId)
            .Distinct().Order(StringComparer.Ordinal).ToArray();
        while (true) {
            pending.RemoveAll(index => locked || (order[index].FirstActivationOnly && activations > 0) || !hand.Contains(index)
                || (order[index].DrawOncePerTurn && used.Contains(order[index].Id))
                || order[index].DrawCount!.Value + order[index].PreDrawDeckRemovalCount + order[index].PostDrawDeckMillCount > order.Count - next);
            // Scan physical candidates in encounter order; retain deferred positions.
            // Independent physical scheduling: visit ranked identities in user order,
            // then all remaining physical candidates in their original encounter order.
            IEnumerable<int> candidates = priorityIds is null ? pending
                : priorityIds.SelectMany(id => pending.Where(index => order[index].Id == id))
                    .Concat(pending.Where(index => !priorityIds.Contains(order[index].Id)));
            int source = candidates.FirstOrDefault(index => order[index].DrawCount!.Value + order[index].PreDrawDeckRemovalCount + order[index].PostDrawDeckMillCount <= order.Count - next && Payable(index), -1);
            if (source < 0) {
                break;
            }
            Card effect = order[source];
            pending.Remove(source);
            for (int cost = 0; cost < effect.PreDrawRemovalCount; cost++) {
                Remove(effect.PreDrawSelector, source);
            }
            // Physically skip these positions; their identities never enter the hand.
            next += effect.PreDrawDeckRemovalCount;
            hand.Remove(source);
            used.Add(effect.Id);
            activations++;
            for (int draw = 0; draw < effect.DrawCount!.Value; draw++) {
                hand.Add(next);
                if (order[next].DrawCount is not null) {
                    pending.Add(next);
                }
                next++;
            }
            locked = effect.BlocksLaterDraws;
            // These are the physical positions AFTER the drawn batch, never observed draws.
            next += effect.PostDrawDeckMillCount;
            for (int discard = 0; discard < effect.PostDrawDiscardCount; discard++) {
                if (!Remove(effect.PostDrawSelector)) {
                    return null;
                }
            }
        }

        bool Payable(int candidate, int reserved = -1) => hand.Count(index => index != candidate && index != reserved
            && MatchesSelector(order[candidate].PreDrawSelector, order[index])) >= order[candidate].PreDrawRemovalCount;

        bool Remove(CardSelector selector, int reserved = -1) {
            HashSet<string> eligibleOnce = new(StringComparer.Ordinal);
            HashSet<int> usable = [];
            HashSet<int> costDeferred = [];
            int remaining = order.Count - next;
            int minimumLegalDrawCount = reserved >= 0 && order[reserved].DrawCount is int reservedDrawCount
                && reservedDrawCount + order[reserved].PreDrawDeckRemovalCount + order[reserved].PostDrawDeckMillCount <= remaining
                ? reservedDrawCount + order[reserved].PreDrawDeckRemovalCount + order[reserved].PostDrawDeckMillCount
                : int.MaxValue;
            foreach (int candidate in pending) {
                Card card = order[candidate];
                if (locked || (card.FirstActivationOnly && (activations > 0 || reserved >= 0))
                    || (reserved >= 0 && order[reserved].BlocksLaterDraws)
                    || !hand.Contains(candidate) || candidate == reserved
                    || (reserved >= 0 && card.DrawOncePerTurn && card.Id == order[reserved].Id)
                    || (card.DrawOncePerTurn && (used.Contains(card.Id) || !eligibleOnce.Add(card.Id)))
                    || card.DrawCount is not int drawCount || drawCount + card.PreDrawDeckRemovalCount + card.PostDrawDeckMillCount > remaining) {
                    continue;
                }

                if (Payable(candidate, reserved)) {
                    usable.Add(candidate);
                    minimumLegalDrawCount = Math.Min(minimumLegalDrawCount, drawCount + card.PreDrawDeckRemovalCount + card.PostDrawDeckMillCount);
                }
                else if (card.PreDrawRemovalCount > 0 && !card.FirstActivationOnly) {
                    int copyCount = hand.Count(index => index != reserved && order[index].Id == card.Id);
                    bool redundant = card.DrawOncePerTurn && copyCount > 1;
                    if (!redundant) {
                        costDeferred.Add(candidate);
                    }
                }
            }
            int remainingAfterAnotherDraw = minimumLegalDrawCount == int.MaxValue
                ? -1
                : remaining - minimumLegalDrawCount;
            HashSet<int> deferred = costDeferred.Where(index =>
                order[index].DrawCount!.Value + order[index].PreDrawDeckRemovalCount + order[index].PostDrawDeckMillCount <= remainingAfterAnotherDraw).ToHashSet();
            List<int> eligible = hand.Where(index => index != reserved && MatchesSelector(selector, order[index])).ToList();
            if (eligible.Count == 0) {
                return false;
            }
            int removed = eligible.OrderByDescending(index => Tier(index))
                .ThenByDescending(index => Membership(order[index]), StringComparer.Ordinal)
                .ThenByDescending(index => order[index].DrawCount is not null)
                .ThenByDescending(index => order[index].Id, StringComparer.Ordinal)
                .ThenByDescending(index => index).First();
            hand.Remove(removed);
            pending.Remove(removed);
            return true;

            int Tier(int index) {
                Card card = order[index];
                bool relevant = combos.Any(combo =>
                    combo.AllCards.Any(requirement => requirement.MinCount > 0 && requirement.CardId == card.Id)
                    || combo.AllCategories.Any(requirement => requirement.MinCount > 0 && MatchesSelector(requirement.Selector, card)));
                // Inspect labeled original copies outside the current hand, never the
                // physical remaining deck. This is the access approximation.
                int capacity = remaining - (reserved < 0 ? 0 : order[reserved].DrawCount!.Value
                    + order[reserved].PreDrawDeckRemovalCount + order[reserved].PostDrawDeckMillCount);
                List<Card> targets = capacity > 0 && card.Search is { } search
                    ? order.Where((target, label) => target.Id != card.Id && !hand.Contains(label)
                        && MatchesSearch(search, target)).ToList() : [];
                bool usefulSearch = targets.Any(target => combos.Any(combo =>
                    combo.AllCards.Any(requirement => requirement.MinCount > 0 && requirement.CardId == target.Id)
                    || combo.AllCategories.Any(requirement => requirement.MinCount > 0 && MatchesSelector(requirement.Selector, target))));
                return (relevant || usefulSearch, usable.Contains(index) || targets.Count > 0, deferred.Contains(index)) switch {
                    (true, _, _) => 1,
                    (_, true, _) => 2,
                    (_, _, true) => 3,
                    _ => 4
                };
            }
        }

        remainingDeck?.Invoke(order.Skip(next).ToArray());
        return hand.Select(index => order[index]).ToList();

        string Membership(Card card) => string.Concat(categoryKeys.Select(key => card.Categories.Any(category => category.Identity == key) ? "1" : "0"))
            + string.Concat(cardKeys.Select(key => card.Id == key ? "1" : "0"));
    }

    internal static bool MatchesSearch(SearchEffectPlan search, Card target) {
        if (!target.Active || target.Copies <= 0 || (search.ObjectiveMetadataOnly && target.ExternalCardId is not > 0)) {
            return false;
        }
        Card inspected = search.ObjectiveMetadataOnly
            ? new(target.Categories.Where(category => category.Source == CategorySource.Metadata
                && !target.ManualMetadataCategoryKeys.Contains(category.MetadataKey!)), externalCardId: target.ExternalCardId) : target;

        return MatchesSelector(search.Selector, inspected);
    }

    internal static bool MatchesSelector(CardSelector selector, Card card) {
        HashSet<string> identities = card.Categories.Select(category => category.Identity).ToHashSet(StringComparer.Ordinal);
        if (selector.Kind == CardSelectorKind.Any) {
            return true;
        }
        if (selector.Children.Count > 0) {
            bool[] results = selector.Children.Select(child => MatchesSelector(child, card)).ToArray();
            return selector.Kind == CardSelectorKind.AllOf ? results.All(result => result) : results.Any(result => result);
        }
        if (selector.Kind == CardSelectorKind.LevelAtLeast) {
            string[] keys = card.Categories.Where(category => category.Source == CategorySource.Metadata
                && !card.ManualMetadataCategoryKeys.Contains(category.MetadataKey!)).Select(category => category.MetadataKey!).ToArray();
            string[] levels = keys.Where(key => key.StartsWith("level:", StringComparison.Ordinal)).ToArray();
            return card.ExternalCardId is > 0 && keys.Contains("kind:monster") && levels.Length == 1
                && !keys.Any(key => key.StartsWith("rank:", StringComparison.Ordinal) || key.StartsWith("link:", StringComparison.Ordinal)
                    || key is "monster-type:xyz" or "monster-type:link")
                && int.TryParse(levels[0][6..], System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out int level) && level >= selector.MinimumLevel;
        }
        int matched = selector.CategoryIdentities.Count(identities.Contains);
        return selector.Kind == CardSelectorKind.AllOf ? matched == selector.CategoryIdentities.Count : matched > 0;
    }

    internal sealed record Counts(
        BigInteger Total,
        BigInteger Failed,
        BigInteger[] Wins,
        Dictionary<string, BigInteger> Scenarios,
        string[] CardIds,
        Dictionary<string, BigInteger> Hands
    );

    internal static Counts Enumerate(List<Card> deck, int handSize, IReadOnlyList<Combo> combos, IReadOnlyList<ComboGroup> groups, bool collapseIdenticalCopies = false, IReadOnlyList<string>? priorityIds = null) {
        Card[] physical = deck.SelectMany(card => Enumerable.Repeat(card, card.Copies)).ToArray();
        int[] order = Enumerable.Range(0, physical.Length).ToArray();
        Card[] effects = deck.Where(card => card.DrawCount is not null && card.Copies > 0).ToArray();
        string[] cardIds = deck.Select(card => card.Id).Distinct(StringComparer.Ordinal).ToArray();
        // Optional multiset enumeration skips only indistinguishable physical-copy
        // permutations. Every retained order stands for the same product of copy factorials.
        BigInteger orderMultiplicity = 1;
        if (collapseIdenticalCopies) {
            foreach (IGrouping<Card, Card> copies in physical.GroupBy(card => card)) {
                for (int i = 2; i <= copies.Count(); i++) {
                    orderMultiplicity *= i;
                }
            }
        }
        BigInteger total = 0;
        BigInteger failed = 0;
        BigInteger[] wins = new BigInteger[1 + combos.Count + groups.Count];
        Dictionary<string, BigInteger> scenarios = new();
        Dictionary<string, BigInteger> hands = new();
        Dictionary<string, bool[]> matches = new();
        Visit(0);
        return new(total, failed, wins, scenarios, cardIds, hands);

        void Visit(int position) {
            if (position == order.Length) {
                Evaluate();
                return;
            }

            HashSet<Card>? seen = collapseIdenticalCopies ? [] : null;
            for (int i = position; i < order.Length; i++) {
                if (seen is not null && !seen.Add(physical[order[i]])) {
                    continue;
                }
                (order[i], order[position]) = (order[position], order[i]);
                Visit(position + 1);
                (order[i], order[position]) = (order[position], order[i]);
            }
        }

        void Evaluate() {
            total += orderMultiplicity;
            List<Card>? hand = Resolve(order.Select(index => physical[index]).ToArray(), handSize, combos, priorityIds);
            if (hand is null) {
                failed += orderMultiplicity;
                return;
            }

            int ordinaryCount = hand.Count(card => card.DrawCount is null);
            string scenario = $"{ordinaryCount}|{string.Join(',', effects.Select(effect => hand.Count(card => card.Id == effect.Id)))}";
            scenarios[scenario] = scenarios.GetValueOrDefault(scenario) + orderMultiplicity;
            string handKey = string.Join(',', cardIds.Select(id => hand.Count(card => card.Id == id)));
            hands[handKey] = hands.GetValueOrDefault(handKey) + orderMultiplicity;
            string matchKey = string.Join(',', deck.Select(entry => hand.Count(card => card.Id == entry.Id)));
            if (!matches.TryGetValue(matchKey, out bool[] success)) {
                bool[] individual = combos.Select(combo => SmallDeckOracle.MatchesHand(hand, combo)).ToArray();
                success = [individual.Any(value => value), .. individual,
                    .. groups.Select(group => combos.Where((combo, index) => combo.GroupId == group.Id && individual[index]).Any())];
                matches.Add(matchKey, success);
            }

            for (int i = 0; i < success.Length; i++) {
                if (success[i]) {
                    wins[i] += orderMultiplicity;
                }
            }

        }
    }
}
