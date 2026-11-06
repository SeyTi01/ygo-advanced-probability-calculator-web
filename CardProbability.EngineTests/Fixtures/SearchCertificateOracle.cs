using System.Numerics;
using CardProbability;

namespace CardProbability.EngineTests.Fixtures;

// Enumerates physical deck orders, hidden-loss allocations, and optional search
// witnesses independently. The five rows are elementary lower, physical,
// optimistic upper, shared-loss robust lower, and no-search.
internal static class SearchCertificateOracle {
    internal sealed record Counts(BigInteger Outcomes, BigInteger Failed, BigInteger[][] Wins);
    private sealed record Reach(bool[] Results, HashSet<string> Hands);

    internal static Counts Enumerate(EngineCase fixture, Action<IReadOnlyList<DeckEntry>, int, int, bool[]>? checkTerminal = null) {
        CalculationRequest request = fixture.Request;
        if (request.Entries.Any(entry => entry.Effect?.Excavation is not null
            || (entry.Effect?.Search is not null && (entry.Effect.HandCost != 0 || entry.Effect.RemovalCount != 0 || entry.Effect.MinimumHandSize != 0)))) {
            throw new NotSupportedException("This witness oracle supports optional searches without search-side hand operations or excavation.");
        }
        OracleModel model = new(request);
        DeckEntry[] entries = request.Entries.Where(entry => entry.Copies > 0).ToArray();
        DeckEntry[] labels = entries.SelectMany(entry => Enumerable.Repeat(entry, entry.Copies)).ToArray();
        int[] permutation = Enumerable.Range(0, labels.Length).ToArray();
        BigInteger multiplicity = BigInteger.One;
        foreach (DeckEntry entry in request.Entries) {
            for (int copy = 2; copy <= entry.Copies; copy++) {
                multiplicity *= copy;
            }
        }
        BigInteger total = 0;
        BigInteger failed = 0;
        BigInteger[][] wins = Enumerable.Range(0, 5)
            .Select(_ => new BigInteger[1 + request.Goals.Length + request.Groups.Length]).ToArray();
        Dictionary<string, bool[][]> cache = new(StringComparer.Ordinal);
        Visit(0);
        return new(total, failed, wins);

        void Visit(int position) {
            if (position == permutation.Length) {
                Evaluate();
                return;
            }
            HashSet<string> seen = new(StringComparer.Ordinal);
            for (int index = position; index < permutation.Length; index++) {
                if (!seen.Add(labels[permutation[index]].Id)) {
                    continue;
                }
                (permutation[index], permutation[position]) = (permutation[position], permutation[index]);
                Visit(position + 1);
                (permutation[index], permutation[position]) = (permutation[position], permutation[index]);
            }
        }

        void Evaluate() {
            total += multiplicity;
            DeckEntry[] order = permutation.Select(index => labels[index]).ToArray();
            List<DeckEntry>? hand = DrawSequenceOracle.Resolve(fixture, model, order, out IReadOnlyList<DeckEntry> rest);
            if (hand is null) {
                failed += multiplicity;
                return;
            }
            int[] held = entries.Select(entry => hand.Count(card => card.Id == entry.Id)).ToArray();
            int[] physical = entries.Select(entry => rest.Count(card => card.Id == entry.Id)).ToArray();
            string key = string.Join(',', held) + "|" + string.Join(',', physical);
            if (!cache.TryGetValue(key, out bool[][]? results)) {
                int lost = labels.Length - hand.Count - rest.Count;
                int[] upper = entries.Select((entry, index) => entry.Copies - held[index]).ToArray();
                int[] lower = upper.Select(count => Math.Max(0, count - lost)).ToArray();
                Reach lowerReach = Search(hand, lower, rest.Count);
                checkTerminal?.Invoke(hand, rest.Count, lost, lowerReach.Results);
                Reach physicalReach = Search(hand, physical, rest.Count);
                Reach upperReach = Search(hand, upper, rest.Count);
                if (!lowerReach.Hands.IsSubsetOf(physicalReach.Hands) || !physicalReach.Hands.IsSubsetOf(upperReach.Hands)) {
                    throw new InvalidOperationException("Search witness inclusion failed.");
                }
                bool[] robust = Enumerable.Repeat(true, upperReach.Results.Length).ToArray();
                int[] survivors = new int[entries.Length];
                Allocate(0, rest.Count);
                bool[] none = Search(hand, new int[entries.Length], 0).Results;
                results = [lowerReach.Results, physicalReach.Results, upperReach.Results, robust, none];
                for (int row = 0; row < robust.Length; row++) {
                    if (lowerReach.Results[row] && !robust[row] || robust[row] && !physicalReach.Results[row]) {
                        throw new InvalidOperationException("Shared-loss bound inclusion failed.");
                    }
                }
                cache.Add(key, results);

                void Allocate(int row, int left) {
                    if (row == survivors.Length) {
                        if (left == 0) {
                            bool[] result = Search(hand, survivors, rest.Count, recordHands: false).Results;
                            for (int index = 0; index < robust.Length; index++) {
                                robust[index] &= result[index];
                            }
                        }
                        return;
                    }
                    for (int count = 0; count <= Math.Min(left, upper[row]); count++) {
                        survivors[row] = count;
                        Allocate(row + 1, left - count);
                    }
                }
            }
            for (int mode = 0; mode < wins.Length; mode++) {
                for (int row = 0; row < wins[mode].Length; row++) {
                    if (results[mode][row]) {
                        wins[mode][row] += multiplicity;
                    }
                }
            }
        }

        Reach Search(IReadOnlyList<DeckEntry> original, int[] capacity, int remaining, bool recordHands = true) {
            DeckEntry[] hand = original.ToArray();
            int[] available = (int[])capacity.Clone();
            Dictionary<string, int> used = new(StringComparer.Ordinal);
            Dictionary<(string Id, int Mode), int> modeUsed = [];
            bool[] matches = new bool[1 + request.Goals.Length + request.Groups.Length];
            HashSet<string> hands = new(StringComparer.Ordinal);
            Assign(0, remaining);
            return new(matches, hands);

            void Assign(int slot, int left) {
                if (slot == hand.Length) {
                    if (recordHands) {
                        hands.Add(string.Join(',', entries.Select(entry => hand.Count(card => card.Id == entry.Id))));
                    }
                    bool[] rows = model.Rows(hand);
                    for (int row = 0; row < rows.Length; row++) {
                        matches[row] |= rows[row];
                    }
                    return;
                }
                Assign(slot + 1, left);
                DeckEntry source = original[slot];
                EffectPlan? effect = source.Effect;
                SearchOperation? search = effect?.Search;
                if (left == 0 || search is null || used.GetValueOrDefault(source.Id) >= (effect!.ActivationLimit ?? int.MaxValue)) {
                    return;
                }
                SearchMode[] modes = search.Modes.IsEmpty ? [new(search.Targets)] : search.Modes.ToArray();
                for (int mode = 0; mode < modes.Length; mode++) {
                    SearchMode choice = modes[mode];
                    (string, int) modeKey = (source.Id, mode);
                    if (modeUsed.GetValueOrDefault(modeKey) >= (choice.ActivationLimit ?? int.MaxValue)) {
                        continue;
                    }
                    for (int target = 0; target < entries.Length; target++) {
                        if (available[target] == 0 || entries[target].Id == source.Id || !model.Eligible(choice.Targets, entries[target])) {
                            continue;
                        }
                        used[source.Id] = used.GetValueOrDefault(source.Id) + 1;
                        modeUsed[modeKey] = modeUsed.GetValueOrDefault(modeKey) + 1;
                        available[target]--;
                        hand[slot] = entries[target];
                        Assign(slot + 1, left - 1);
                        hand[slot] = source;
                        available[target]++;
                        used[source.Id]--;
                        modeUsed[modeKey]--;
                    }
                }
            }
        }
    }
}
