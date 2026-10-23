using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.DrawResolution;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Counting;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture, NonParallelizable]
public sealed class PhysicalOrderedHardCaseTest {
    private sealed record Fixture(List<Card> Cards, int Hand, List<Combo> Combos, List<ComboGroup> Groups);
    private sealed record OracleResult(BigInteger Orders, BigInteger Failed, BigInteger[] Wins,
        Dictionary<string, BigInteger> Physical);

    [TestCase("overlapping-targets")]
    [TestCase("filtered-failure")]
    [TestCase("hidden-before-and-after")]
    [TestCase("searcher-targets-searcher")]
    [TestCase("minimum-equal")]
    [TestCase("minimum-too-high")]
    [TestCase("capacity-equal")]
    [TestCase("capacity-too-short")]
    [TestCase("empty-opener")]
    [TestCase("hand-cost-fallback")]
    [TestCase("multiple-source-copies-fallback")]
    public void CombinedDrawsAndLossCountingMatchIndependentPhysicalOrders(string name) {
        Fixture fixture = Create(name);
        OracleResult oracle = Enumerate(fixture);
        foreach (bool pool in new[] { false, true }) {
            foreach (PhysicalEnumerationMethod method in new[] { PhysicalEnumerationMethod.Auto, PhysicalEnumerationMethod.SingleSource }) {
                ExactCalculationResult actual = Calculate(fixture, method, pool);
                AssertRows(actual, oracle, name + "/" + method + "/pool=" + pool);
            }
        }
        if (name == "filtered-failure") {
            Assert.That(oracle.Failed, Is.GreaterThan(BigInteger.Zero));
            Assert.That(oracle.Failed, Is.LessThan(oracle.Orders));
        }
    }

    [TestCase("overlapping-targets")]
    [TestCase("filtered-failure")]
    [TestCase("hidden-before-and-after")]
    public void CombinedBatchPreservesEveryHeldAndPhysicalSurvivorOutcome(string name) {
        Fixture fixture = Create(name);
        OracleResult oracle = Enumerate(fixture);
        foreach (PhysicalEnumerationMethod method in new[] { PhysicalEnumerationMethod.SingleSource, PhysicalEnumerationMethod.BatchStreaming }) {
            Dictionary<string, BigInteger> actual = new(StringComparer.Ordinal);
            Calculate(fixture, method, false, (cards, state, available, mass) => {
                OrderedDrawStateLayout layout = new(cards);
                Assert.That(state[layout.DrawsLeft], Is.Zero);
                Assert.That(state[layout.CompletionSource], Is.Zero);
                string key = PhysicalKey(cards, state.AsSpan(layout.HandStart, cards.Count), available);
                actual[key] = actual.GetValueOrDefault(key) + mass;
            });
            Assert.That(actual, Is.EquivalentTo(oracle.Physical), name + "/" + method);
            Assert.That(actual.Values.Aggregate(BigInteger.Zero, (sum, mass) => sum + mass) + oracle.Failed,
                Is.EqualTo(oracle.Orders), "Mandatory failures remain in the original labeled denominator.");
        }
    }

    [Test]
    public void SamePrintedNameAndEntryOrderDoNotEraseDirectIdentityOrEmptyGroups() {
        Fixture fixture = Create("overlapping-targets");
        OracleResult oracle = Enumerate(fixture);
        fixture.Cards.Reverse();
        OracleResult reversed = Enumerate(fixture);
        Assert.That(reversed.Wins, Is.EqualTo(oracle.Wins));
        Assert.That(reversed.Physical, Is.EquivalentTo(oracle.Physical));
        ExactCalculationResult result = Calculate(fixture, PhysicalEnumerationMethod.SingleSource, true);
        AssertRows(result, oracle, "reversed encounter order");
        Assert.That(result.Groups[^1].Numerator, Is.EqualTo(BigInteger.Zero), "An authored empty group has no successful witness.");
    }

    private static Fixture Create(string name) {
        CategoryBase first = new("First");
        CategoryBase second = new("Second");
        CategoryBase payment = new("Payment");
        CategoryBase searcher = new("Searcher");
        List<Card> cards = [
            new([first], id: "draw", drawCount: 2, postDrawDiscardCount: 1, postDrawDeckMillCount: 1),
            new([searcher], id: "search-a", search: new(CardSelector.For(first), false)),
            new([searcher], id: "search-b", search: new(CardSelector.For(second), false)),
            new([first, payment], name: "Same printed name", id: "target-a"),
            new([second], name: "Same printed name", id: "target-b"),
            new([first, second], id: "target-both"),
            new([], id: "filler-a"),
            new([], id: "filler-b")
        ];
        int hand = 3;
        switch (name) {
            case "filtered-failure":
                cards[0] = new([first], id: "draw", drawCount: 2, postDrawDiscardCount: 2,
                    postDrawSelector: CardSelector.For(first), postDrawDeckMillCount: 1);
                break;
            case "hidden-before-and-after":
                cards[0] = new([first], id: "draw", drawCount: 2, preDrawDeckRemovalCount: 1,
                    postDrawDiscardCount: 1, postDrawDeckMillCount: 1, firstActivationOnly: true, blocksLaterDraws: true);
                break;
            case "searcher-targets-searcher":
                cards[1] = new([searcher], id: "search-a", search: new(CardSelector.For(searcher), false));
                break;
            case "minimum-equal":
            case "minimum-too-high":
                cards[0] = new([first], id: "draw", drawCount: 2, postDrawDiscardCount: 1,
                    postDrawDeckMillCount: 1, minimumHandSize: name == "minimum-equal" ? hand : hand + 1);
                break;
            case "capacity-equal":
            case "capacity-too-short":
                cards.RemoveAt(cards.Count - 1);
                if (name == "capacity-too-short") {
                    cards.RemoveAt(cards.Count - 1);
                }
                cards[0] = new([first], id: "draw", drawCount: 2, postDrawDiscardCount: 1, postDrawDeckMillCount: 2);
                break;
            case "empty-opener":
                hand = 0;
                break;
            case "hand-cost-fallback":
                cards[0] = new([first], id: "draw", drawCount: 2, preDrawRemovalCount: 1,
                    preDrawSelector: CardSelector.For(payment), postDrawDiscardCount: 1, postDrawDeckMillCount: 1);
                break;
            case "multiple-source-copies-fallback":
                cards[0] = cards[0].WithCopies(2);
                cards.RemoveAt(cards.Count - 1);
                break;
        }
        List<Combo> combos = [
            new([new(first, 1, 2), new(second, 1, 2)], "Distinct overlapping pieces", groupId: "pieces"),
            new([new(first, 0, 0)], "No first piece", groupId: "pieces"),
            new([], "Only this identity", cards: [new("target-a", 1, 3), new("target-b", 0, 0)], groupId: "identity"),
            new([], "Source consumed or absent", cards: [new("draw", 0, 0)], groupId: "identity"),
            new([], "Completed branch", groupId: "complete")
        ];
        List<ComboGroup> groups = [new("pieces", "Pieces"), new("identity", "Identities"),
            new("complete", "Completed"), new("empty", "Empty")];
        return new(cards, hand, combos, groups);
    }

    private static ExactCalculationResult Calculate(
        Fixture fixture,
        PhysicalEnumerationMethod method,
        bool pool,
        Action<IReadOnlyList<Card>, int[], int[], BigInteger>? observer = null
    ) {
        WorkBudget budget = new(CalculationWorkPolicy.Unlimited);
        PostDrawCalculation calculation = new(fixture.Cards, fixture.Hand, budget, deferIdentities: false) {
            ObservePhysicalOutcome = observer
        };
        return calculation.CalculateSearch(fixture.Combos, fixture.Groups, false, new(fixture.Cards, budget),
            physical: true, method: method, coalesceOrdinary: pool);
    }

    // Enumerate actual labeled orders. DrawSequenceOracle implements physical
    // draw/removal scheduling independently of every production resolver. This
    // oracle then searches physical copies directly; it has no binomial weights,
    // projected capacities, production event compiler or search-decision reuse.
    private static OracleResult Enumerate(Fixture fixture) {
        Card[] labels = fixture.Cards.SelectMany(card => Enumerable.Repeat(card, card.Copies)).ToArray();
        Card[] effects = fixture.Cards.Where(card => card.DrawCount is not null).ToArray();
        int[] permutation = Enumerable.Range(0, labels.Length).ToArray();
        BigInteger orders = BigInteger.Zero;
        BigInteger failed = BigInteger.Zero;
        BigInteger[] wins = new BigInteger[1 + fixture.Combos.Count + fixture.Groups.Count];
        Dictionary<string, BigInteger> physical = new(StringComparer.Ordinal);
        Dictionary<string, bool[]> evaluated = new(StringComparer.Ordinal);
        Permute(0);
        return new(orders, failed, wins, physical);

        void Permute(int position) {
            if (position < permutation.Length) {
                for (int next = position; next < permutation.Length; next++) {
                    (permutation[position], permutation[next]) = (permutation[next], permutation[position]);
                    Permute(position + 1);
                    (permutation[position], permutation[next]) = (permutation[next], permutation[position]);
                }
                return;
            }
            orders++;
            Card[] order = permutation.Select(label => labels[label]).ToArray();
            IReadOnlyList<Card> remaining = [];
            List<Card>? hand;
            if (effects.Length == 1 && effects[0].Copies == 1 && effects[0].MinimumHandSize > fixture.Hand) {
                // With one physical source and no other draw, an unmet opening
                // minimum cannot become payable. The older general oracle does
                // not model minimum-hand restrictions, so handle this boundary
                // directly from the labeled opener without modifying its policy.
                hand = order.Take(fixture.Hand).ToList();
                remaining = order.Skip(fixture.Hand).ToArray();
            }
            else {
                hand = DrawSequenceOracle.Resolve(order, fixture.Hand, fixture.Combos, remainingDeck: rest => remaining = rest);
            }
            if (hand is null) {
                failed++;
                return;
            }
            int[] held = fixture.Cards.Select(card => hand.Count(copy => copy.Id == card.Id)).ToArray();
            int[] available = fixture.Cards.Select(card => remaining.Count(copy => copy.Id == card.Id)).ToArray();
            string key = PhysicalKey(fixture.Cards, held, available);
            physical[key] = physical.GetValueOrDefault(key) + 1;
            if (!evaluated.TryGetValue(key, out bool[]? success)) {
                success = SearchPhysicalCopies(hand, remaining, fixture.Combos);
                evaluated.Add(key, success);
            }
            bool[] rows = [success.Any(value => value), .. success,
                .. fixture.Groups.Select(group => fixture.Combos.Where((combo, index) => combo.GroupId == group.Id && success[index]).Any())];
            for (int row = 0; row < rows.Length; row++) {
                if (rows[row]) {
                    wins[row]++;
                }
            }
        }
    }

    private static bool[] SearchPhysicalCopies(IReadOnlyList<Card> original, IReadOnlyList<Card> deck, List<Combo> combos) {
        Card[] hand = original.ToArray();
        bool[] taken = new bool[deck.Count];
        bool[] success = new bool[combos.Count];
        Dictionary<string, int> activations = new(StringComparer.Ordinal);
        Visit(0);
        return success;

        void Visit(int position) {
            if (position == original.Count) {
                for (int combo = 0; combo < combos.Count; combo++) {
                    success[combo] |= SmallDeckOracle.MatchesHand(hand, combos[combo]);
                }
                return;
            }
            Visit(position + 1);
            Card source = original[position];
            int used = activations.GetValueOrDefault(source.Id);
            if (source.Search is not { } search || used >= source.MaximumActivations) {
                return;
            }
            for (int target = 0; target < deck.Count; target++) {
                if (taken[target] || deck[target].Id == source.Id || !DrawSequenceOracle.MatchesSearch(search, deck[target])) {
                    continue;
                }
                taken[target] = true;
                hand[position] = deck[target];
                activations[source.Id] = used + 1;
                Visit(position + 1);
                activations[source.Id] = used;
                hand[position] = source;
                taken[target] = false;
            }
        }
    }

    private static string PhysicalKey(IReadOnlyList<Card> cards, ReadOnlySpan<int> held, ReadOnlySpan<int> available) {
        List<string> values = [];
        for (int row = 0; row < cards.Count; row++) {
            values.Add(cards[row].Id + ":" + held[row] + "," + available[row]);
        }
        values.Sort(StringComparer.Ordinal);
        return string.Join(";", values);
    }

    private static void AssertRows(ExactCalculationResult actual, OracleResult expected, string description) {
        ExactProbability[] rows = [actual.Total, .. actual.Combos, .. actual.Groups];
        Assert.That(rows, Has.Length.EqualTo(expected.Wins.Length), description);
        for (int row = 0; row < rows.Length; row++) {
            Assert.That(rows[row].Numerator * expected.Orders, Is.EqualTo(expected.Wins[row] * rows[row].Denominator),
                description + "/row=" + row);
        }
    }
}
