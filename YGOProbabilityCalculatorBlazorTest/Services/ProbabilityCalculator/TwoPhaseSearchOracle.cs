using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

// Independent labeled-card oracle. Finish every fixed draw transition before
// evaluating optional search witnesses, with no production scheduler or compiler.
internal static class TwoPhaseSearchOracle {
    internal readonly record struct Rational(BigInteger Numerator, BigInteger Denominator) {
        internal static Rational Zero => new(0, 1);
        internal static Rational One => new(1, 1);
        internal Rational Add(Rational other) => Reduce(Numerator * other.Denominator + other.Numerator * Denominator,
            Denominator * other.Denominator);
        internal Rational Scale(int numerator, int denominator) => Reduce(Numerator * numerator, Denominator * denominator);
        internal Rational Max(Rational other) => Numerator * other.Denominator >= other.Numerator * Denominator ? this : other;
        private static Rational Reduce(BigInteger numerator, BigInteger denominator) {
            BigInteger divisor = BigInteger.GreatestCommonDivisor(numerator, denominator);
            return new(numerator / divisor, denominator / divisor);
        }
    }

    private sealed record Token(int Label, int Row, int Encounter);
    private sealed class World {
        internal List<Token> Hand = [];
        internal List<Token> Pending = [];
        internal List<int> Deck = [];
        internal int[] Used = [];
        internal int[][] Modes = [];
        internal int NextEncounter;
        internal int DrawActivations;
        internal bool DrawLocked;
        internal bool DrawComplete;
        internal bool Failed;

        internal World Clone() => new() {
            Hand = [.. Hand], Pending = [.. Pending], Deck = [.. Deck], Used = (int[])Used.Clone(),
            Modes = Modes.Select(mode => (int[])mode.Clone()).ToArray(), NextEncounter = NextEncounter,
            DrawActivations = DrawActivations, DrawLocked = DrawLocked, DrawComplete = DrawComplete, Failed = Failed
        };
    }

    internal static Rational[] Count(IReadOnlyList<Card> input, int handSize, IReadOnlyList<Combo> combos,
        IReadOnlyList<ComboGroup> groups, DrawActivationPriority priority) {
        Card[] cards = input.Where(card => card.Active && card.Copies > 0).ToArray();
        int[] labels = cards.SelectMany((card, row) => Enumerable.Repeat(row, card.Copies)).ToArray();
        string[] categoryKeys = combos.SelectMany(combo => combo.AllCategories).SelectMany(requirement => requirement.CategoryBases)
            .Select(category => category.Identity).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        string[] directKeys = combos.SelectMany(combo => combo.AllCards).Select(requirement => requirement.CardId)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        bool[] relevant = cards.Select(card => combos.Any(combo =>
            combo.AllCards.Any(requirement => requirement.MinCount > 0 && requirement.CardId == card.Id)
            || combo.AllCategories.Any(requirement => requirement.MinCount > 0 && Matches(requirement.Selector, card)))).ToArray();
        if (labels.Length > 8) {
            throw new ArgumentException("The independent labeled oracle is restricted to eight physical cards.");
        }
        Dictionary<string, List<World>> openers = [];
        int[] order = Enumerable.Range(0, labels.Length).ToArray();
        Enumerate(0);
        Func<IReadOnlyList<Card>, bool>[] individual = combos.Select(combo =>
            (Func<IReadOnlyList<Card>, bool>)(hand => SearchCertificateHandOracle.MatchesHand(hand, combo))).ToArray();
        Func<IReadOnlyList<Card>, bool>[] objectives = [hand => individual.Any(match => match(hand)), .. individual,
            .. groups.Select(group => (Func<IReadOnlyList<Card>, bool>)(hand => combos.Select((combo, index) => (combo, index))
                .Any(entry => entry.combo.GroupId == group.Id && individual[entry.index](hand))))];
        int total = openers.Values.Sum(worlds => worlds.Count);
        return objectives.Select(objective => openers.Values.Aggregate(Rational.Zero,
            (sum, worlds) => sum.Add(Resolve(worlds, objective).Scale(worlds.Count, total)))).ToArray();

        void Enumerate(int position) {
            if (position < order.Length) {
                for (int index = position; index < order.Length; index++) {
                    (order[position], order[index]) = (order[index], order[position]);
                    Enumerate(position + 1);
                    (order[position], order[index]) = (order[index], order[position]);
                }
                return;
            }
            World world = new() {
                Hand = order.Take(handSize).Select((label, encounter) => new Token(label, labels[label], encounter)).ToList(),
                Deck = order.Skip(handSize).ToList(), Used = new int[cards.Length],
                Modes = cards.Select(card => new int[card.Search?.Modes?.Count ?? 1]).ToArray(), NextEncounter = handSize
            };
            world.Pending = world.Hand.Where(token => Enabled(cards[token.Row])).ToList();
            string observation = Observation(world);
            if (!openers.TryGetValue(observation, out List<World>? worlds)) {
                openers.Add(observation, worlds = []);
            }
            worlds.Add(world);
        }

        Rational Resolve(List<World> worlds, Func<IReadOnlyList<Card>, bool> objective) {
            World known = worlds[0];
            if (known.Failed) {
                return Rational.Zero;
            }
            if (known.Pending.Any(token => Expired(known, token))) {
                worlds = worlds.Select(world => {
                    World next = world.Clone();
                    next.Pending.RemoveAll(token => Expired(next, token));
                    return next;
                }).ToList();
                known = worlds[0];
            }
            foreach (Token candidate in known.Pending.Where(token => known.DrawComplete == (cards[token.Row].Search is not null)).OrderBy(token => Rank(cards[token.Row].Id)).ThenBy(token => token.Encounter)) {
                Card effect = cards[candidate.Row];
                if (known.Used[candidate.Row] >= effect.MaximumActivations || known.Hand.Count < effect.MinimumHandSize
                    || known.Hand.Count(token => token.Encounter != candidate.Encounter
                        && Matches(effect.PreDrawSelector, cards[token.Row])) < effect.PreDrawRemovalCount) {
                    continue;
                }
                if (effect.DrawCount is int draws) {
                    if (known.DrawLocked || effect.FirstActivationOnly && known.DrawActivations > 0
                        || known.Deck.Count < draws + effect.PreDrawDeckRemovalCount + effect.PostDrawDeckMillCount) {
                        continue;
                    }
                    List<World> after = worlds.Select(world => Draw(world, candidate.Encounter)).ToList();
                    return AverageObserved(after, objective);
                }
                SearchEffectPlan search = effect.Search!;
                SearchEffectMode[] modes = search.Modes?.ToArray() ?? [new(search.Selector)];
                if (!modes.Where((mode, index) => known.Modes[candidate.Row][index] < (mode.ActivationLimit ?? int.MaxValue)).Any()) {
                    continue;
                }
                // Inspecting the Main Deck exposes counts, never a permutation.
                List<List<World>> inspections = worlds.GroupBy(world => string.Join(',',
                    Enumerable.Range(0, cards.Length).Select(row => world.Deck.Count(label => labels[label] == row))))
                    .Select(group => group.ToList()).ToList();
                if (inspections.Count > 1) {
                    return inspections.Aggregate(Rational.Zero,
                        (sum, observed) => sum.Add(Resolve(observed, objective).Scale(observed.Count, worlds.Count)));
                }
                List<(int Mode, int Target)> actions = [];
                for (int mode = 0; mode < modes.Length; mode++) {
                    if (known.Modes[candidate.Row][mode] >= (modes[mode].ActivationLimit ?? int.MaxValue)) {
                        continue;
                    }
                    for (int row = 0; row < cards.Length; row++) {
                        if (row != candidate.Row && known.Deck.Any(label => labels[label] == row)
                            && SearchMatches(search, modes[mode].Selector, cards[row])) {
                            actions.Add((mode, row));
                        }
                    }
                }
                if (actions.Count == 0) {
                    continue;
                }
                List<World> declined = worlds.Select(world => {
                    World next = world.Clone();
                    next.Pending.RemoveAll(token => token.Encounter == candidate.Encounter);
                    return next;
                }).ToList();
                Rational best = Resolve(declined, objective);
                foreach ((int mode, int target) in actions) {
                    List<World> after = worlds.Select(world => Search(world, candidate.Encounter, mode, target)).ToList();
                    best = best.Max(AverageObserved(after, objective));
                }
                return best;
            }
            if (!known.DrawComplete) {
                foreach (World world in worlds) {
                    world.DrawComplete = true;
                    world.Pending.RemoveAll(token => cards[token.Row].DrawCount is not null);
                }
                return Resolve(worlds, objective);
            }
            return objective(known.Hand.Select(token => cards[token.Row]).ToArray()) ? Rational.One : Rational.Zero;
        }

        Rational AverageObserved(List<World> worlds, Func<IReadOnlyList<Card>, bool> objective) => worlds.GroupBy(Observation)
            .Aggregate(Rational.Zero, (sum, group) => sum.Add(Resolve(group.ToList(), objective).Scale(group.Count(), worlds.Count)));

        World Draw(World original, int encounter) {
            World next = original.Clone();
            Token source = next.Hand.Single(token => token.Encounter == encounter);
            Card effect = cards[source.Row];
            next.Pending.Remove(source);
            RemoveFixed(next, effect.PreDrawRemovalCount, effect.PreDrawSelector, source);
            Consume(next, source);
            next.Deck.RemoveRange(0, effect.PreDrawDeckRemovalCount);
            for (int draw = 0; draw < effect.DrawCount!.Value; draw++) {
                int label = next.Deck[0];
                next.Deck.RemoveAt(0);
                Token drawn = new(label, labels[label], next.NextEncounter++);
                next.Hand.Add(drawn);
                if (Enabled(cards[drawn.Row])) {
                    next.Pending.Add(drawn);
                }
            }
            next.DrawActivations++;
            next.DrawLocked |= effect.BlocksLaterDraws;
            next.Deck.RemoveRange(0, effect.PostDrawDeckMillCount);
            RemoveFixed(next, effect.PostDrawDiscardCount, effect.PostDrawSelector);
            return next;
        }

        World Search(World original, int encounter, int mode, int target) {
            World next = original.Clone();
            Token source = next.Hand.Single(token => token.Encounter == encounter);
            Card effect = cards[source.Row];
            Consume(next, source);
            next.Modes[source.Row][mode]++;
            RemoveFixed(next, effect.PreDrawRemovalCount, effect.PreDrawSelector);
            // A fixed label, independent of deck position. Removing the FIRST
            // matching position would bias later draws when copies are repeated.
            int label = next.Deck.Where(label => labels[label] == target).Min();
            next.Deck.Remove(label);
            next.Hand.Add(new(label, target, next.NextEncounter++));
            RemoveFixed(next, effect.PostDrawDiscardCount, effect.PostDrawSelector);
            return next;
        }

        void Consume(World world, Token source) {
            world.Hand.Remove(source);
            world.Pending.Remove(source);
            world.Used[source.Row]++;
        }

        void RemoveFixed(World world, int count, CardSelector selector, Token? reserved = null) {
            for (int removal = 0; removal < count; removal++) {
                List<Token> choices = world.Hand.Where(token => token != reserved && Matches(selector, cards[token.Row])).ToList();
                if (choices.Count == 0) {
                    world.Failed = true;
                    return;
                }
                HashSet<int> payableDraws = [];
                HashSet<int> deferredDraws = [];
                int minimumNextDraw = reserved is not null && cards[reserved.Row].DrawCount is not null
                    ? Consumption(cards[reserved.Row]) : int.MaxValue;
                foreach (Token pending in world.Pending) {
                    Card effect = cards[pending.Row];
                    if (world.DrawComplete || effect.DrawCount is null || !DrawTiming(world, effect) || Allowance(pending.Row) <= 0
                        || Consumption(effect) > world.Deck.Count
                        || reserved is not null && cards[reserved.Row].DrawCount is not null
                            && (cards[reserved.Row].BlocksLaterDraws || effect.FirstActivationOnly)) {
                        continue;
                    }
                    bool payable = world.Hand.Count - (reserved is null ? 0 : 1) >= effect.MinimumHandSize
                        && world.Hand.Count(token => token != pending && token != reserved
                            && Matches(effect.PreDrawSelector, cards[token.Row])) >= effect.PreDrawRemovalCount;
                    if (payable) {
                        payableDraws.Add(pending.Row);
                        minimumNextDraw = Math.Min(minimumNextDraw, Consumption(effect));
                    }
                    else if (effect.PreDrawRemovalCount > 0 || effect.MinimumHandSize > 0) {
                        deferredDraws.Add(pending.Row);
                    }
                }
                Token removed = choices.OrderByDescending(token => Tier(token.Row))
                    .ThenByDescending(token => Membership(cards[token.Row]), StringComparer.Ordinal)
                    .ThenByDescending(token => Enabled(cards[token.Row]) && cards[token.Row].DrawCount is not null)
                    .ThenByDescending(token => cards[token.Row].Id, StringComparer.Ordinal)
                    // Within one entry preserve naturally eligible copies before
                    // inert fetched/declined copies, then preserve earlier FIFO.
                    .ThenBy(token => world.Pending.Contains(token))
                    .ThenByDescending(token => token.Encounter).First();
                world.Hand.Remove(removed);
                world.Pending.Remove(removed);

                int Allowance(int row) => cards[row].MaximumActivations - world.Used[row]
                    - (reserved?.Row == row ? 1 : 0);

                int Tier(int row) {
                    Card card = cards[row];
                    int held = world.Hand.Count(token => token.Row == row && token != reserved);
                    bool redundant = held > Allowance(row);
                    bool deferred = deferredDraws.Contains(row) && !redundant && !card.FirstActivationOnly
                        && minimumNextDraw != int.MaxValue && Consumption(card) <= world.Deck.Count - minimumNextDraw;
                    int tier = relevant[row] ? 1 : payableDraws.Contains(row) && !redundant ? 2 : deferred ? 3 : 4;
                    int searchCapacity = world.Deck.Count - (reserved is null ? 0 : Consumption(cards[reserved.Row]));
                    if (card.Search is not { } search || !world.Pending.Any(token => token.Row == row)
                        || Allowance(row) <= 0 || searchCapacity <= 0) {
                        return tier;
                    }
                    SearchEffectMode[] modes = search.Modes?.ToArray() ?? [new(search.Selector)];
                    for (int mode = 0; mode < modes.Length; mode++) {
                        if (world.Modes[row][mode] >= (modes[mode].ActivationLimit ?? int.MaxValue)) {
                            continue;
                        }
                        for (int target = 0; target < cards.Length; target++) {
                            // Plausibility uses original copies minus held copies.
                            // Inspecting hidden survivors must not change removal.
                            if (target != row && cards[target].Copies > world.Hand.Count(token => token.Row == target)
                                && SearchMatches(search, modes[mode].Selector, cards[target])) {
                                tier = Math.Min(tier, relevant[target] ? 1 : 2);
                            }
                        }
                    }
                    return tier;
                }
            }
        }

        bool Expired(World world, Token token) => world.Used[token.Row] >= cards[token.Row].MaximumActivations
            || !DrawTiming(world, cards[token.Row]) || Consumption(cards[token.Row]) > world.Deck.Count;
        bool DrawTiming(World world, Card card) => card.DrawCount is null
            || !world.DrawLocked && (!card.FirstActivationOnly || world.DrawActivations == 0);
        int Consumption(Card card) => card.Search is not null ? 1
            : card.DrawCount!.Value + card.PreDrawDeckRemovalCount + card.PostDrawDeckMillCount;
        string Membership(Card card) => string.Concat(categoryKeys.Select(key => card.Categories.Any(category => category.Identity == key) ? '1' : '0'))
            + string.Concat(directKeys.Select(id => card.Id == id ? '1' : '0'));

        int Rank(string id) => priority.Mode == DrawActivationMode.CustomPriority
            ? priority.CardIds.Select((value, index) => (value, index)).Where(entry => entry.value == id)
                .Select(entry => entry.index).DefaultIfEmpty(int.MaxValue).First() : 0;
        bool Enabled(Card card) => card.SimulationEnabled && card.HasEffect;
        string Observation(World world) => world.Failed ? "failed"
            : string.Join(',', world.Hand.Select(token => $"{token.Row}:{token.Encounter}")) + ";"
                + string.Join(',', world.Pending.Select(token => token.Encounter)) + ";" + string.Join(',', world.Used) + ";"
                + string.Join('|', world.Modes.Select(mode => string.Join(',', mode)))
                + $";{world.NextEncounter};{world.DrawActivations};{world.DrawLocked};{world.DrawComplete};{world.Deck.Count}";
    }

    private static bool SearchMatches(SearchEffectPlan search, CardSelector selector, Card card) =>
        !search.ObjectiveMetadataOnly ? Matches(selector, card)
            : card.ExternalCardId is > 0 && Matches(selector, ObjectiveCard(card));

    private static bool Matches(CardSelector selector, Card card) => selector.Kind switch {
        CardSelectorKind.Not => !Matches(selector.Children[0], card),
        CardSelectorKind.Objective => card.ExternalCardId is > 0 && Matches(selector.Children[0], ObjectiveCard(card)),
        CardSelectorKind.AllOf when selector.Children.Count > 0 => selector.Children.All(child => Matches(child, card)),
        CardSelectorKind.AnyOf when selector.Children.Count > 0 => selector.Children.Any(child => Matches(child, card)),
        _ => DrawSequenceOracle.MatchesSelector(selector, card)
    };

    private static Card ObjectiveCard(Card card) => new(card.Categories.Where(category => category.Source == CategorySource.Metadata
        && !card.ManualMetadataCategoryKeys.Contains(category.MetadataKey!)), externalCardId: card.ExternalCardId);
}
