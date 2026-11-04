using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

// Each physical copy is labeled. Enumerating deck permutations exposes a real
// top-card pool; this oracle never uses the solver's successful-target formula.
internal static class ExcavationOracle {
    internal sealed record Counts(BigInteger Outcomes, BigInteger[] Wins);

    internal static Counts Count(
        IReadOnlyList<Card> input,
        int handSize,
        IReadOnlyList<Combo> combos,
        IReadOnlyList<ComboGroup> groups,
        IReadOnlyDictionary<string, string[]> searchTargets,
        DrawActivationPriority? priority = null
    ) {
        Card[] cards = input.Where(card => card.Active && card.Copies > 0).ToArray();
        Card[] labels = cards.SelectMany(card => Enumerable.Repeat(card, card.Copies)).ToArray();
        if (labels.Length > 8 || cards.Any(card => card.DrawCount is not null && (card.PreDrawRemovalCount > 0 || card.PostDrawDiscardCount > 0)
            || card.Search?.Modes is not null) || cards.Count(card => card.EffectPlan.RequiresSearchResolution) > 1) {
            throw new ArgumentException("The excavation oracle supports at most eight cards, simple draws and at most one bounded search.");
        }
        Func<IReadOnlyList<Card>, bool>[][] predicates = Enumerable.Range(0, labels.Length + 1)
            .Select(size => combos.Select(combo => SmallDeckOracle.HandPredicate(combo, size)).ToArray()).ToArray();
        int[][] groupRows = groups.Select(group => combos.Select((combo, index) => (combo, index))
            .Where(entry => entry.combo.GroupId == group.Id).Select(entry => entry.index).ToArray()).ToArray();
        BigInteger[] wins = new BigInteger[1 + combos.Count + groups.Count];
        BigInteger outcomes = 0;
        int[] order = Enumerable.Range(0, labels.Length).ToArray();
        Enumerate(0);

        return new(outcomes, wins);

        void Enumerate(int position) {
            if (position < order.Length) {
                for (int index = position; index < order.Length; index++) {
                    (order[position], order[index]) = (order[index], order[position]);
                    Enumerate(position + 1);
                    (order[position], order[index]) = (order[index], order[position]);
                }

                return;
            }
            List<int> hand = order.Take(handSize).ToList();
            List<int> remaining = order.Skip(handSize).ToList();
            Dictionary<string, int> drawsUsed = new(StringComparer.Ordinal);
            bool drew = false;
            bool drawLocked = false;
            while (!drawLocked) {
                int source = hand.Where(label => labels[label].SimulationEnabled && labels[label].DrawCount is not null)
                    .OrderBy(label => DrawRank(labels[label].Id)).FirstOrDefault(label => {
                        Card effect = labels[label];
                        return (!effect.FirstActivationOnly || !drew)
                            && drawsUsed.GetValueOrDefault(effect.Id) < effect.MaximumActivations
                            && remaining.Count >= effect.PreDrawDeckRemovalCount + effect.DrawCount!.Value + effect.PostDrawDeckMillCount;
                    }, -1);
                if (source < 0) {
                    break;
                }
                Card draw = labels[source];
                hand.Remove(source);
                remaining.RemoveRange(0, draw.PreDrawDeckRemovalCount);
                hand.AddRange(remaining.Take(draw.DrawCount!.Value));
                remaining.RemoveRange(0, draw.DrawCount.Value);
                remaining.RemoveRange(0, draw.PostDrawDeckMillCount);
                drawsUsed[draw.Id] = drawsUsed.GetValueOrDefault(draw.Id) + 1;
                drew = true;
                drawLocked = draw.BlocksLaterDraws;
            }

            // Selection depends only on the observed post-draw hand and public
            // eligibility; no revealed cards participate in source priority.
            int excavation = hand.Where(label => labels[label].SimulationEnabled && labels[label].Excavation is { } plan
                    && remaining.Count >= plan.Count && (!plan.RequiresNoPriorEffectDraw || !drew))
                .OrderByDescending(label => labels[label].Excavation!.Count)
                .ThenBy(label => labels[label].Id, StringComparer.Ordinal).FirstOrDefault(-1);
            int[] searches = hand.Where(label => labels[label].SimulationEnabled && labels[label].Search is not null)
                .OrderBy(label => DrawRank(labels[label].Id)).ToArray();
            bool[] matched = new bool[combos.Count];
            if (excavation < 0) {
                Search(0, hand, remaining, new(StringComparer.Ordinal));
            }
            else {
                // Activation is mandatory once selected. Exactly one pool copy
                // enters hand; all other copies stay searchable in the Deck.
                hand.Remove(excavation);
                foreach (int target in remaining.Take(labels[excavation].Excavation!.Count)) {
                    List<int> nextHand = [.. hand, target];
                    List<int> nextDeck = remaining.Where(label => label != target).ToList();
                    Search(0, nextHand, nextDeck, new(StringComparer.Ordinal));
                }
            }
            outcomes++;
            if (matched.Any(value => value)) {
                wins[0]++;
            }
            for (int index = 0; index < matched.Length; index++) {
                if (matched[index]) {
                    wins[index + 1]++;
                }
            }
            for (int group = 0; group < groupRows.Length; group++) {
                if (groupRows[group].Any(index => matched[index])) {
                    wins[1 + combos.Count + group]++;
                }
            }

            void Search(int index, List<int> currentHand, List<int> currentDeck, Dictionary<string, int> used) {
                if (index == searches.Length) {
                    Card[] finalHand = currentHand.Select(label => labels[label]).ToArray();
                    for (int combo = 0; combo < matched.Length; combo++) {
                        matched[combo] |= predicates[finalHand.Length][combo](finalHand);
                    }

                    return;
                }
                int source = searches[index];
                Search(index + 1, currentHand, currentDeck, used);
                Card effect = labels[source];
                if (!currentHand.Contains(source) || used.GetValueOrDefault(effect.Id) >= effect.MaximumActivations
                    || currentHand.Count < effect.MinimumHandSize) {
                    return;
                }
                foreach (int target in currentDeck.Where(label => searchTargets[effect.Id].Contains(labels[label].Id))) {
                    List<int> nextHand = currentHand.Where(label => label != source).ToList();
                    if (!RemoveUnique(nextHand, effect.PreDrawRemovalCount, effect.PreDrawSelector)) {
                        continue;
                    }
                    nextHand.Add(target);
                    if (!RemoveUnique(nextHand, effect.PostDrawDiscardCount, effect.PostDrawSelector)) {
                        continue;
                    }
                    List<int> nextDeck = currentDeck.Where(label => label != target).ToList();
                    Dictionary<string, int> nextUsed = new(used, StringComparer.Ordinal) {
                        [effect.Id] = used.GetValueOrDefault(effect.Id) + 1
                    };
                    // The search list was frozen before excavation. Neither a
                    // revealed source nor a searched source gets a second pass.
                    Search(index + 1, nextHand, nextDeck, nextUsed);
                }
            }

            bool RemoveUnique(List<int> currentHand, int count, CardSelector selector) {
                if (count == 0) {
                    return true;
                }
                int[] eligible = currentHand.Where(label => DrawSequenceOracle.MatchesSelector(selector, labels[label])).ToArray();
                if (eligible.Length < count) {
                    return false;
                }
                if (eligible.Length != count) {
                    throw new ArgumentException("Oracle fixtures require a unique filtered removal, without a keep-policy choice.");
                }
                currentHand.RemoveAll(eligible.Contains);

                return true;
            }
        }

        int DrawRank(string id) => priority?.Mode == DrawActivationMode.CustomPriority
            && Array.IndexOf(priority.CardIds.ToArray(), id) is int rank && rank >= 0 ? rank : int.MaxValue;
    }
}
