using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

internal static class SmallDeckOracle {

    // Independent exhaustive slot assignment, without Hall subsets, masks,
    // count-vector DP or production helpers. Each position can be used once.
    internal static bool MatchesHand(IReadOnlyList<Card> hand, Combo combo) => HandPredicate(combo, hand.Count)(hand);

    internal static Func<IReadOnlyList<Card>, bool> HandPredicate(Combo combo, int handSize) {
        // Independently choose each alternative and then assign physical copies
        // across the WHOLE conjunction. No production expression/compiler helper.
        if (combo.AlternativeGroups.Count > 0) {
            var predicates = new List<Func<IReadOnlyList<Card>, bool>>();
            Choose(0, combo.Categories.ToList(), combo.Cards.ToList());
            return hand => predicates.Any(predicate => predicate(hand));
            void Choose(int index, List<ComboCategory> categories, List<ComboCard> cards) {
                if (index == combo.AlternativeGroups.Count) {
                    predicates.Add(HandPredicate(new Combo(categories, cards: cards), handSize));
                    return;
                }
                foreach (var alternative in combo.AlternativeGroups[index].Alternatives) {
                    var nextCategories = categories.ToList();
                    var nextCards = cards.ToList();
                    if (alternative.Kind == "Category") nextCategories.Add(alternative.Category!);
                    else if (alternative.Kind == "Card") nextCards.Add(alternative.Card!);
                    else throw new ArgumentException("Unknown oracle alternative.");
                    Choose(index + 1, nextCategories, nextCards);
                }
            }
        }
        var roles = new List<(Func<Card, bool> Matches, int Min, int Max)>();
        foreach (var group in combo.Categories.GroupBy(c => (c.BaseCategory.Source, Key: c.BaseCategory.Source == CategorySource.User ? c.BaseCategory.Name : c.BaseCategory.MetadataKey)))
            roles.Add((card => card.Categories.Any(c => c.Source == group.Key.Source && (c.Source == CategorySource.User ? c.Name : c.MetadataKey) == group.Key.Key),
                group.Max(c => c.MinCount), group.Min(c => c.MaximumMode == RequirementMaximumMode.HandSize ? handSize : c.MaxCount)));
        foreach (var group in combo.Cards.GroupBy(c => c.CardId))
            roles.Add((card => card.Id == group.Key,
                group.Max(c => c.MinCount), group.Min(c => c.MaximumMode == RequirementMaximumMode.HandSize ? handSize : c.MaxCount)));
        var slots = roles.SelectMany(role => Enumerable.Repeat(role.Matches, role.Min)).ToArray();
        return hand => {
            if (roles.Any(role => role.Min > role.Max || hand.Count(role.Matches) > role.Max)) return false;
            if (slots.Length > hand.Count) return false;
            var used = new bool[hand.Count];
            return Assign(0);

            bool Assign(int slot) {
                if (slot == slots.Length) return true;
                for (var copy = 0; copy < hand.Count; copy++) {
                    if (used[copy] || !slots[slot](hand[copy])) continue;
                    used[copy] = true;
                    if (Assign(slot + 1)) return true;
                    used[copy] = false;
                }
                return false;
            }
        };
    }

    // Each physical copy has its own position. Visit each unordered hand exactly once;
    // directly evaluate OR-of-combos / AND-of-constraints, without masks, merging,
    // binomial coefficients, inclusion-exclusion, or production helper methods.
    internal static double EnumerateProbability(List<Card> deck, List<Combo> combos, int handSize) {
        var (successes, total) = EnumerateCounts(deck, combos, handSize);
        return (double)successes / total;
    }

    internal static (int Successes, int Total) EnumerateCounts(List<Card> deck, List<Combo> combos, int handSize) {
        var predicates = combos.Select(combo => HandPredicate(combo, handSize)).ToArray();
        var copies = new List<Card>();
        foreach (var card in deck)
            for (var i = 0; i < card.Copies; i++) copies.Add(card);
        var hand = new List<Card>();
        var total = 0;
        var successes = 0;
        Visit(0);
        return (successes, total);

        void Visit(int start) {
            if (hand.Count == handSize) {
                total++;
                if (predicates.Any(predicate => predicate(hand))) successes++;
                return;
            }
            for (var i = start; i <= copies.Count - (handSize - hand.Count); i++) {
                hand.Add(copies[i]);
                Visit(i + 1);
                hand.RemoveAt(hand.Count - 1);
            }
        }
    }

}
