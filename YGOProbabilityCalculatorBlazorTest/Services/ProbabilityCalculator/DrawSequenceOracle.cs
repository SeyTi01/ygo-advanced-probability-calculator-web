using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

// Enumerate labeled physical permutations, deal the opener, then resolve queued
// effects in batches. No production scenario, mask, Hall or fraction helpers.
internal static class DrawSequenceOracle {
    internal sealed record Counts(BigInteger Total, BigInteger Failed, BigInteger[] Wins, Dictionary<string, BigInteger> Scenarios);

    internal static Counts Enumerate(List<Card> deck, int handSize, IReadOnlyList<Combo> combos, IReadOnlyList<ComboGroup> groups) {
        Card[] physical = deck.SelectMany(card => Enumerable.Repeat(card, card.Copies)).ToArray();
        int[] order = Enumerable.Range(0, physical.Length).ToArray();
        Card[] effects = deck.Where(card => card.DrawCount is not null && card.Copies > 0).ToArray();
        BigInteger total = 0;
        BigInteger failed = 0;
        BigInteger[] wins = new BigInteger[1 + combos.Count + groups.Count];
        Dictionary<string, BigInteger> scenarios = new();
        Dictionary<string, bool[]> matches = new();
        Visit(0);
        return new(total, failed, wins, scenarios);

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
            List<Card> hand = [];
            HashSet<string> used = new(StringComparer.Ordinal);
            Queue<int> pendingEffects = new();
            int next = handSize;
            for (int i = 0; i < handSize; i++) {
                Draw(physical[order[i]]);
            }

            while (pendingEffects.TryDequeue(out int drawCount)) {
                if (physical.Length - next < drawCount) {
                    failed++;
                    return;
                }

                for (int i = 0; i < drawCount; i++) {
                    Draw(physical[order[next++]]);
                }
            }

            int ordinaryCount = hand.Count(card => card.DrawCount is null);
            string scenario = $"{ordinaryCount}|{string.Join(',', effects.Select(effect => hand.Count(card => card.Id == effect.Id)))}";
            scenarios[scenario] = scenarios.GetValueOrDefault(scenario) + 1;
            string handKey = string.Join(',', deck.Select(entry => hand.Count(card => card.Id == entry.Id)));
            if (!matches.TryGetValue(handKey, out bool[] success)) {
                bool[] individual = combos.Select(combo => SmallDeckOracle.MatchesHand(hand, combo)).ToArray();
                success = [individual.Any(value => value), .. individual,
                    .. groups.Select(group => combos.Where((combo, index) => combo.GroupId == group.Id && individual[index]).Any())];
                matches.Add(handKey, success);
            }

            for (int i = 0; i < success.Length; i++) {
                if (success[i]) {
                    wins[i]++;
                }
            }

            void Draw(Card card) {
                if (card.DrawCount is { } count && used.Add(card.Id)) {
                    pendingEffects.Enqueue(count);
                }
                else {
                    hand.Add(card);
                }
            }
        }
    }
}
