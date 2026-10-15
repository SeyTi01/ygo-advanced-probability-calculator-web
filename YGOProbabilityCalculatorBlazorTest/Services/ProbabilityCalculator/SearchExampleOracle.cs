using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

// Independent weighted physical-hand enumeration for the maintained example.
// Eligibility/OPT are fixed from official text, not the production search plan.
// No event preimages, production selectors or probability engine are used.
internal static class SearchExampleOracle {
    internal static long[] Count(SessionState session) {
        Card[] deck = session.Cards.ToArray();
        Card[] hand = new Card[session.HandSize];
        int[] original = new int[hand.Length];
        int[] held = new int[deck.Length];
        int[] available = new int[deck.Length];
        Dictionary<int, string[]> targetNames = new() {
            [32807846] = ["Vanquish Soul Razen"],
            [80181649] = ["K9-04 Noroi", "K9-17 Izuna", "K9-66a Jokul", "K9-66b Lantern", "K9-ØØ Lupis"],
            [92221402] = ["K9-66a Jokul", "K9-66b Lantern"]
        };
        int[][] targets = deck.Select(card => targetNames.TryGetValue(card.ExternalCardId ?? 0, out string[]? names)
            ? deck.Select((entry, index) => (entry, index)).Where(entry => names.Contains(entry.entry.Name)).Select(entry => entry.index).ToArray()
            : Array.Empty<int>()).ToArray();
        Func<IReadOnlyList<Card>, bool>[] predicates = session.Combos.Select(combo => SmallDeckOracle.HandPredicate(combo, hand.Length)).ToArray();
        int[] groupMasks = session.ComboGroups.Select(group => session.Combos.Select((combo, index) => (combo, index))
            .Where(entry => entry.combo.GroupId == group.Id).Aggregate(0, (mask, entry) => mask | (1 << entry.index))).ToArray();
        long[] counts = new long[2 + session.Combos.Count + groupMasks.Length];
        int matched = 0;
        Deal(0, 0, 1);
        return counts;

        void Deal(int row, int dealt, long weight) {
            if (dealt == hand.Length) {
                for (int i = row; i < held.Length; i++) {
                    held[i] = 0;
                }
                for (int i = 0; i < deck.Length; i++) {
                    available[i] = deck[i].Copies - held[i];
                }
                matched = 0;
                Search(0, 0);
                counts[0] += weight;
                if (matched != 0) {
                    counts[1] += weight;
                }
                for (int i = 0; i < predicates.Length; i++) {
                    if ((matched & (1 << i)) != 0) {
                        counts[2 + i] += weight;
                    }
                }
                for (int i = 0; i < groupMasks.Length; i++) {
                    if ((matched & groupMasks[i]) != 0) {
                        counts[2 + predicates.Length + i] += weight;
                    }
                }
                return;
            }
            if (row == deck.Length) {
                return;
            }
            long combinations = 1;
            for (int amount = 0; amount <= Math.Min(deck[row].Copies, hand.Length - dealt); amount++) {
                held[row] = amount;
                for (int slot = 0; slot < amount; slot++) {
                    hand[dealt + slot] = deck[row];
                    original[dealt + slot] = row;
                }
                Deal(row + 1, dealt + amount, weight * combinations);
                combinations = combinations * (deck[row].Copies - amount) / (amount + 1);
            }
        }

        void Search(int slot, int usedNames) {
            if (slot == hand.Length) {
                for (int combo = 0; combo < predicates.Length; combo++) {
                    if ((matched & (1 << combo)) == 0 && predicates[combo](hand)) {
                        matched |= 1 << combo;
                    }
                }
                return;
            }
            Search(slot + 1, usedNames);
            int row = original[slot];
            int bit = deck[row].ExternalCardId switch { 80181649 => 1, 92221402 => 2, _ => 0 };
            if (bit != 0 && (usedNames & bit) != 0) {
                return;
            }
            foreach (int target in targets[row]) {
                if (available[target] == 0) {
                    continue;
                }
                available[target]--;
                hand[slot] = deck[target];
                Search(slot + 1, usedNames | bit);
                hand[slot] = deck[row];
                available[target]++;
            }
        }
    }
}
