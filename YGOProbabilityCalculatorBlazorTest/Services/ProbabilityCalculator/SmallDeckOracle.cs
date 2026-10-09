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
            List<Func<IReadOnlyList<Card>, bool>> predicates = [];
            Choose(0, combo.Categories.ToList(), combo.Cards.ToList());
            return hand => predicates.Any(predicate => predicate(hand));

            void Choose(int index, List<ComboCategory> categories, List<ComboCard> cards) {
                if (index == combo.AlternativeGroups.Count) {
                    predicates.Add(HandPredicate(new Combo(categories, cards: cards), handSize));

                    return;
                }

                foreach (ComboAlternative alternative in combo.AlternativeGroups[index].Alternatives) {
                    List<ComboCategory> nextCategories = categories.ToList();
                    List<ComboCard> nextCards = cards.ToList();

                    if (alternative.Kind == "Category") {
                        nextCategories.Add(alternative.Category!);
                    } else if (alternative.Kind == "Card") {
                        nextCards.Add(alternative.Card!);
                    } else {
                        throw new ArgumentException("Unknown oracle alternative.");
                    }

                    Choose(index + 1, nextCategories, nextCards);
                }
            }
        }
        List<(Func<Card, bool> Matches, int Min, int Max)> roles = [];

        foreach (IGrouping<(CategorySource Source, string? Key), ComboCategory> group in combo.Categories.GroupBy(
            category => (
                category.BaseCategory.Source,
                Key: category.BaseCategory.Source == CategorySource.User
                    ? category.BaseCategory.Name
                    : category.BaseCategory.MetadataKey
            ))) {
            roles.Add((
                card => card.Categories.Any(category =>
                    category.Source == group.Key.Source
                    && (category.Source == CategorySource.User ? category.Name : category.MetadataKey) == group.Key.Key),
                group.Max(constraint => constraint.MinCount),
                group.Min(constraint => constraint.MaximumMode == RequirementMaximumMode.HandSize ? handSize : constraint.MaxCount)
            ));
        }

        foreach (IGrouping<string, ComboCard> group in combo.Cards.GroupBy(constraint => constraint.CardId)) {
            roles.Add((
                card => card.Id == group.Key,
                group.Max(constraint => constraint.MinCount),
                group.Min(constraint => constraint.MaximumMode == RequirementMaximumMode.HandSize ? handSize : constraint.MaxCount)
            ));
        }

        Func<Card, bool>[] slots = roles.SelectMany(role => Enumerable.Repeat(role.Matches, role.Min)).ToArray();

        return hand => {
            bool hasInvalidOrOverfilledRole = roles.Any(role => role.Min > role.Max || hand.Count(role.Matches) > role.Max);

            if (hasInvalidOrOverfilledRole) {
                return false;
            }

            if (slots.Length > hand.Count) {
                return false;
            }

            bool[] used = new bool[hand.Count];
            return Assign(0);

            bool Assign(int slot) {
                if (slot == slots.Length) {
                    return true;
                }

                for (int copy = 0; copy < hand.Count; copy++) {
                    if (used[copy] || !slots[slot](hand[copy])) {
                        continue;
                    }

                    used[copy] = true;

                    if (Assign(slot + 1)) {
                        return true;
                    }

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
        (int successes, int total) = EnumerateCounts(deck, combos, handSize);
        return (double)successes / total;
    }

    internal static (int Successes, int Total) EnumerateCounts(List<Card> deck, List<Combo> combos, int handSize) {
        Func<IReadOnlyList<Card>, bool>[] predicates = combos.Select(combo => HandPredicate(combo, handSize)).ToArray();
        List<Card> copies = [];

        foreach (Card card in deck) {
            for (int i = 0; i < card.Copies; i++) {
                copies.Add(card);
            }
        }

        List<Card> hand = [];
        int total = 0;
        int successes = 0;
        Visit(0);

        return (successes, total);

        void Visit(int start) {
            if (hand.Count == handSize) {
                total++;

                if (predicates.Any(predicate => predicate(hand))) {
                    successes++;
                }

                return;
            }

            for (int i = start; i <= copies.Count - (handSize - hand.Count); i++) {
                hand.Add(copies[i]);
                Visit(i + 1);
                hand.RemoveAt(hand.Count - 1);
            }
        }
    }
}
