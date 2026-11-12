using System.Numerics;
using CardProbability;

namespace CardProbability.EngineTests.Fixtures;

// Exhaust labeled deck orders and execute each draw in sequence. This does not
// use solver scenarios, packing, event compilation, or probability arithmetic.
internal static class DrawSequenceOracle {
    internal sealed record Counts(BigInteger Total, BigInteger Failed, BigInteger[] Wins);

    internal static Counts Enumerate(EngineCase fixture) {
        CalculationRequest request = fixture.Request;
        if (request.Entries.Any(entry => entry.Effect?.Search is not null || entry.Effect?.Excavation is not null)) {
            throw new NotSupportedException("The draw-only oracle does not evaluate search or excavation.");
        }
        OracleModel model = new(request);
        DeckEntry[] labels = request.Entries.SelectMany(entry => Enumerable.Repeat(entry, entry.Copies)).ToArray();
        int[] permutation = Enumerable.Range(0, labels.Length).ToArray();
        BigInteger multiplicity = BigInteger.One;
        foreach (DeckEntry entry in request.Entries) {
            for (int copy = 2; copy <= entry.Copies; copy++) {
                multiplicity *= copy;
            }
        }
        BigInteger total = 0;
        BigInteger failed = 0;
        BigInteger[] wins = new BigInteger[1 + request.Goals.Length + request.Groups.Length];
        Visit(0);
        return new(total, failed, wins);

        void Visit(int position) {
            if (position == permutation.Length) {
                total += multiplicity;
                DeckEntry[] order = permutation.Select(index => labels[index]).ToArray();
                List<DeckEntry>? hand = Resolve(fixture, model, order, out _);
                if (hand is null) {
                    failed += multiplicity;
                    return;
                }
                bool[] results = model.Rows(hand);
                for (int row = 0; row < results.Length; row++) {
                    if (results[row]) {
                        wins[row] += multiplicity;
                    }
                }
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
    }

    internal static List<DeckEntry>? Resolve(EngineCase fixture, OracleModel model, IReadOnlyList<DeckEntry> order, out IReadOnlyList<DeckEntry> remainingDeck) {
        int handSize = fixture.Request.HandSize;
        List<int> hand = Enumerable.Range(0, handSize).ToList();
        List<int> pending = hand.Where(index => order[index].Effect?.DrawCount is not null).ToList();
        Dictionary<string, int> used = new(StringComparer.Ordinal);
        int next = handSize;
        int activations = 0;
        bool locked = false;
        while (true) {
            pending.RemoveAll(index => !hand.Contains(index) || !TimingAllows(index) || !HasAllowance(index)
                || Consumption(index) > order.Count - next);
            IEnumerable<int> candidates = fixture.Options.ActivationOrder == ActivationOrder.Priority
                ? fixture.Options.PriorityEntryIds.SelectMany(id => pending.Where(index => order[index].Id == id))
                    .Concat(pending.Where(index => !fixture.Options.PriorityEntryIds.Contains(order[index].Id)))
                : pending;
            int source = candidates.FirstOrDefault(index => HandAllows(index) && Payable(index), -1);
            if (source < 0) {
                break;
            }
            EffectPlan effect = order[source].Effect!;
            pending.Remove(source);
            for (int cost = 0; cost < effect.HandCost; cost++) {
                if (!Remove(effect.HandCostEligibility, source)) {
                    throw new InvalidOperationException("A previously payable cost became unavailable.");
                }
            }
            next += effect.HiddenDeckCost;
            hand.Remove(source);
            used[order[source].Id] = used.GetValueOrDefault(order[source].Id) + 1;
            activations++;
            for (int draw = 0; draw < effect.DrawCount!.Value; draw++) {
                hand.Add(next);
                if (order[next].Effect?.DrawCount is not null) {
                    pending.Add(next);
                }
                next++;
            }
            locked = effect.BlocksLaterDraws;
            next += effect.HiddenPostDrawMill;
            for (int discard = 0; discard < effect.RemovalCount; discard++) {
                if (!Remove(effect.RemovalEligibility)) {
                    remainingDeck = [];
                    return null;
                }
            }
        }
        remainingDeck = order.Skip(next).ToArray();
        return hand.Select(index => order[index]).ToList();

        long Consumption(int index) {
            EffectPlan effect = order[index].Effect!;
            return (long)effect.DrawCount!.Value + effect.HiddenDeckCost + effect.HiddenPostDrawMill;
        }
        bool TimingAllows(int index) => !locked && (!order[index].Effect!.FirstActivationOnly || activations == 0);
        bool HasAllowance(int index) => order[index].Effect!.ActivationLimit is not int limit || used.GetValueOrDefault(order[index].Id) < limit;
        bool HandAllows(int index, int reserved = 0) => hand.Count - reserved >= order[index].Effect!.MinimumHandSize;
        bool Payable(int index, int reserved = -1) => hand.Count(other => other != index && other != reserved
            && model.Eligible(order[index].Effect!.HandCostEligibility, order[other])) >= order[index].Effect!.HandCost;

        bool Remove(Eligibility? selector, int reserved = -1) {
            int remaining = order.Count - next;
            long minimumConsumption = reserved >= 0 && Consumption(reserved) <= remaining ? Consumption(reserved) : long.MaxValue;
            HashSet<int> usable = [];
            HashSet<int> costDeferred = [];
            foreach (int candidate in pending) {
                if (!hand.Contains(candidate) || candidate == reserved || !TimingAllows(candidate) || !HasAllowance(candidate)
                    || (reserved >= 0 && (order[reserved].Effect!.BlocksLaterDraws || order[candidate].Effect!.FirstActivationOnly))
                    || Consumption(candidate) > remaining) {
                    continue;
                }
                int limit = order[candidate].Effect!.ActivationLimit ?? int.MaxValue;
                int consumed = used.GetValueOrDefault(order[candidate].Id) + (reserved >= 0 && order[reserved].Id == order[candidate].Id ? 1 : 0);
                if (consumed >= limit) {
                    continue;
                }
                if (HandAllows(candidate, reserved >= 0 ? 1 : 0) && Payable(candidate, reserved)) {
                    usable.Add(candidate);
                    minimumConsumption = Math.Min(minimumConsumption, Consumption(candidate));
                }
                else if (order[candidate].Effect!.HandCost > 0 || order[candidate].Effect!.MinimumHandSize > 0) {
                    costDeferred.Add(candidate);
                }
            }
            long afterAnother = minimumConsumption == long.MaxValue ? -1 : remaining - minimumConsumption;
            List<int> eligible = hand.Where(index => index != reserved && model.Eligible(selector, order[index])).ToList();
            if (eligible.Count == 0) {
                return false;
            }
            int removed = eligible.OrderByDescending(Tier)
                .ThenByDescending(index => model.OrderKey(order[index]), StringComparer.Ordinal)
                .ThenByDescending(index => order[index].Effect?.DrawCount is not null)
                .ThenByDescending(index => order[index].Id, StringComparer.Ordinal)
                .ThenByDescending(index => index).First();
            hand.Remove(removed);
            pending.Remove(removed);
            return true;

            int Tier(int index) {
                DeckEntry card = order[index];
                bool relevant = Relevant(card);
                int limit = card.Effect?.ActivationLimit ?? int.MaxValue;
                int allowance = limit - used.GetValueOrDefault(card.Id) - (reserved >= 0 && order[reserved].Id == card.Id ? 1 : 0);
                bool redundant = hand.Count(other => other != reserved && order[other].Id == card.Id) > allowance;
                bool deferred = costDeferred.Contains(index) && !redundant && !card.Effect!.FirstActivationOnly
                    && Consumption(index) <= afterAnother;
                long capacity = remaining - (reserved < 0 ? 0 : Consumption(reserved));
                bool searchAvailable = capacity > 0 && card.Effect?.Search is { } search
                    && order.Where((target, label) => !hand.Contains(label) && target.Id != card.Id)
                        .Any(target => model.Eligible(search.Targets, target));
                bool usefulSearch = searchAvailable && order.Where((target, label) => !hand.Contains(label) && target.Id != card.Id)
                    .Any(target => model.Eligible(card.Effect!.Search!.Targets, target) && Relevant(target));
                return (relevant || usefulSearch, (usable.Contains(index) && !redundant) || searchAvailable, deferred) switch {
                    (true, _, _) => 1,
                    (_, true, _) => 2,
                    (_, _, true) => 3,
                    _ => 4
                };
            }
        }

        bool Relevant(DeckEntry entry) => fixture.Request.Goals.Any(goal => model.AllRequirements(goal)
            .Any(requirement => requirement.Minimum > 0 && model.Matches(requirement, entry)));
    }
}
