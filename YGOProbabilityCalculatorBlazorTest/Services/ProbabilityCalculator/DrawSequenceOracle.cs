using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

// Enumerate labeled physical permutations, deal the opener, then resolve queued
// effects in batches. No production scenario, mask, Hall or fraction helpers.
internal static class DrawSequenceOracle {
    // Physical labels distinguish copies; encounter order breaks ties within an ID.
    internal static List<Card>? Resolve(IReadOnlyList<Card> order, int handSize, IReadOnlyList<Combo> combos) {
        List<int> hand = Enumerable.Range(0, handSize).ToList();
        Queue<int> pending = new(hand.Where(index => order[index].DrawCount is not null));
        HashSet<string> used = new(StringComparer.Ordinal);
        int next = handSize;
        string[] categoryKeys = combos.SelectMany(combo => combo.AllCategories).SelectMany(requirement => requirement.CategoryBases.Select(category => category.Identity))
            .Distinct().Order(StringComparer.Ordinal).ToArray();
        string[] cardKeys = combos.SelectMany(combo => combo.AllCards).Select(requirement => requirement.CardId)
            .Distinct().Order(StringComparer.Ordinal).ToArray();
        while (pending.TryDequeue(out int source)) {
            Card effect = order[source];
            if (!hand.Contains(source) || (effect.DrawOncePerTurn && used.Contains(effect.Id))) {
                continue;
            }

            hand.Remove(source);
            used.Add(effect.Id);
            int drawCount = Math.Min(effect.DrawCount!.Value, order.Count - next);
            for (int draw = 0; draw < drawCount; draw++) {
                hand.Add(next);
                if (order[next].DrawCount is not null) {
                    pending.Enqueue(next);
                }
                next++;
            }

            if (hand.Count < effect.PostDrawDiscardCount) {
                return null;
            }

            for (int discard = 0; discard < effect.PostDrawDiscardCount; discard++) {
                HashSet<string> eligibleOnce = new(StringComparer.Ordinal);
                HashSet<int> usable = [];
                foreach (int candidate in pending) {
                    Card card = order[candidate];
                    if (hand.Contains(candidate)
                        && (!card.DrawOncePerTurn || (!used.Contains(card.Id) && eligibleOnce.Add(card.Id)))) {
                        usable.Add(candidate);
                    }
                }

                // Independent flat-selector evaluation on labeled physical cards.
                List<int> eligible = hand.Where(index => MatchesSelector(effect.PostDrawSelector, order[index])).ToList();
                if (eligible.Count == 0) {
                    return null;
                }
                int removed = eligible.OrderByDescending(index => Tier(index))
                    .ThenByDescending(index => Membership(order[index]), StringComparer.Ordinal)
                    .ThenByDescending(index => order[index].DrawCount is not null)
                    .ThenByDescending(index => order[index].Id, StringComparer.Ordinal)
                    .ThenByDescending(index => index).First();
                hand.Remove(removed);

                int Tier(int index) {
                    Card card = order[index];
                    bool relevant = combos.Any(combo =>
                        combo.AllCards.Any(requirement => requirement.MinCount > 0 && requirement.CardId == card.Id)
                        || combo.AllCategories.Any(requirement => requirement.MinCount > 0 && requirement.CategoryBases.All(required => card.Categories.Any(category => category.Identity == required.Identity))));
                    return relevant ? 1 : usable.Contains(index) ? 2 : 3;
                }
            }
        }

        return hand.Select(index => order[index]).ToList();

        string Membership(Card card) => string.Concat(categoryKeys.Select(key => card.Categories.Any(category => category.Identity == key) ? "1" : "0"))
            + string.Concat(cardKeys.Select(key => card.Id == key ? "1" : "0"));
    }

    internal static bool MatchesSelector(CardSelector selector, Card card) {
        HashSet<string> identities = card.Categories.Select(category => category.Identity).ToHashSet(StringComparer.Ordinal);
        if (selector.Kind == CardSelectorKind.Any) {
            return true;
        }
        if (selector.Children.Count > 0) {
            return selector.Children.Any(child => MatchesSelector(child, card));
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

    internal static Counts Enumerate(List<Card> deck, int handSize, IReadOnlyList<Combo> combos, IReadOnlyList<ComboGroup> groups) {
        Card[] physical = deck.SelectMany(card => Enumerable.Repeat(card, card.Copies)).ToArray();
        int[] order = Enumerable.Range(0, physical.Length).ToArray();
        Card[] effects = deck.Where(card => card.DrawCount is not null && card.Copies > 0).ToArray();
        string[] cardIds = deck.Select(card => card.Id).Distinct(StringComparer.Ordinal).ToArray();
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

            for (int i = position; i < order.Length; i++) {
                (order[i], order[position]) = (order[position], order[i]);
                Visit(position + 1);
                (order[i], order[position]) = (order[position], order[i]);
            }
        }

        void Evaluate() {
            total++;
            List<Card>? hand = Resolve(order.Select(index => physical[index]).ToArray(), handSize, combos);
            if (hand is null) {
                failed++;
                return;
            }

            int ordinaryCount = hand.Count(card => card.DrawCount is null);
            string scenario = $"{ordinaryCount}|{string.Join(',', effects.Select(effect => hand.Count(card => card.Id == effect.Id)))}";
            scenarios[scenario] = scenarios.GetValueOrDefault(scenario) + 1;
            string handKey = string.Join(',', cardIds.Select(id => hand.Count(card => card.Id == id)));
            hands[handKey] = hands.GetValueOrDefault(handKey) + 1;
            string matchKey = string.Join(',', deck.Select(entry => hand.Count(card => card.Id == entry.Id)));
            if (!matches.TryGetValue(matchKey, out bool[] success)) {
                bool[] individual = combos.Select(combo => SmallDeckOracle.MatchesHand(hand, combo)).ToArray();
                success = [individual.Any(value => value), .. individual,
                    .. groups.Select(group => combos.Where((combo, index) => combo.GroupId == group.Id && individual[index]).Any())];
                matches.Add(matchKey, success);
            }

            for (int i = 0; i < success.Length; i++) {
                if (success[i]) {
                    wins[i]++;
                }
            }

        }
    }
}
