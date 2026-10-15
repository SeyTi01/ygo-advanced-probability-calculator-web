using System.Diagnostics;
using System.Numerics;
using Microsoft.JSInterop;
using Moq;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.Session;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public sealed class SearchEffectsTest {
    private static readonly CategoryBase A = new("A");
    private static readonly CategoryBase B = new("B");
    private static readonly CategoryBase Spell = new("Spell", CategorySource.Metadata, "kind:spell");
    private static readonly ComboGroup[] Groups = [new("g1", "First"), new("g2", "Second")];

    internal static Card Warrior(int copies = 1, int level = 4, string race = "warrior", string id = "target") => new([
        A, B, new("Monster", CategorySource.Metadata, "kind:monster"),
        new("Warrior", CategorySource.Metadata, $"monster-race:{race}"),
        new($"Level {level}", CategorySource.Metadata, $"level:{level}")
    ], copies, id, id: id, externalCardId: 29302858);

    internal static Card Rota(int copies = 1) => CardEffectRegistry.Default.RestoreBuiltIn(
        new([Spell], copies, "ROTA", id: "rota", externalCardId: 32807846));

    [TestCase(1, 1)]
    [TestCase(2, 1)]
    [TestCase(2, 2)]
    [TestCase(2, 0)]
    public void PhysicalCopiesOptionalActivationExclusionsAndOverlappingRows(int searchers, int targets) {
        List<Card> cards = [Rota(searchers), Warrior(targets), new([B], 1, id: "b"), new([], 1, id: "blank")];
        List<Combo> combos = [
            new([], "target", groupId: "g1", cards: [new("target", 1, 5)]),
            new([], "two targets", groupId: "g1", cards: [new("target", 2, 5)]),
            new([new(Spell, 1, 5)], "keep source", groupId: "g2"),
            new([new(Spell, 1, 5)], "source and target", groupId: "g2", cards: [new("target", 1, 5)]),
            new([new(A, 0, 0), new(B, 1, 5)], "exclude target", groupId: "g1"),
            new([new(A, 1, 1), new(B, 1, 5)], "distinct with maximum", groupId: "g2"),
            new([ComboCategory.SameCard([A, B], 1, 5)], "same card"),
            new([], "OR", alternativeGroups: [new([ComboAlternative.For(new ComboCard("target", 1, 5)), ComboAlternative.For(new ComboCategory(Spell, 1, 5))])])
        ];
        foreach (int size in Enumerable.Range(0, Math.Min(3, cards.Sum(card => card.Copies)) + 1)) {
            Compare(cards, combos, size);
        }
        cards.Reverse();
        Compare(cards, combos, 2);
    }

    [Test]
    public void DifferentOverlappingSelectorsAndRecursiveSearchersTerminate() {
        Card first = new Card([B], 2, id: "first").WithSearch(new(CardSelector.For(A), false));
        Card second = new Card([A], 1, id: "second").WithSearch(new(CardSelector.AnyOf([A.Identity, B.Identity]), false));
        List<Card> cards = [first, second, new([A, B], 1, id: "both"), new([B], 1, id: "b"), new([], 1, id: "blank")];
        List<Combo> combos = [new([new(A, 2, 2)], groupId: "g1"), new([new(B, 2, 2)], groupId: "g1"),
            new([new(A, 0, 0)], groupId: "g2", cards: [new("b", 1, 2)]),
            new([], cards: [new("both", 1, 1), new("b", 1, 1)])];
        Compare(cards, combos, 2);
        cards.Reverse();
        Compare(cards, combos, 2);
    }

    [Test]
    public void MaintainedEligibilityUsesVerifiedObjectiveProperties() {
        SearchEffectPlan search = Rota().Search!;
        Card target = Warrior();
        Assert.Multiple(() => {
            Assert.That(search.Matches(target), Is.True);
            Assert.That(search.Matches(Warrior(level: 1)), Is.True);
            Assert.That(search.Matches(Warrior(level: 5)), Is.False);
            Assert.That(search.Matches(Warrior(level: 0)), Is.False);
            Assert.That(search.Matches(Warrior(race: "spellcaster")), Is.False);
            Assert.That(search.Matches(target.WithActive(false)), Is.False);
            Assert.That(search.Matches(target.WithCopies(0)), Is.False);
            Assert.That(search.Matches(new(target.Categories)), Is.False);
            Assert.That(search.Matches(new([Spell], externalCardId: 1)), Is.False);
            Assert.That(search.Matches(new Card([], externalCardId: 1)
                .WithManualMetadataCategory(target.Categories[2]).WithManualMetadataCategory(target.Categories[3])
                .WithManualMetadataCategory(target.Categories[4])), Is.False);
            Assert.That(Rota().DrawOncePerTurn, Is.False);
        });
        Compare([Rota(), Warrior(level: 5), new([], 2)], [new([], cards: [new("target", 1, 2)])], 2);
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void DrawThenSearchUsesActualSurvivorsAndRemainingDeck(int removal) {
        Card draw = new([], 1, id: "draw", drawCount: 2, postDrawDiscardCount: removal,
            postDrawSelector: removal > 0 ? CardSelector.For(Spell) : null);
        List<Card> cards = [Rota(), Warrior(), draw, new([], 2, id: "blank")];
        List<Combo> combos = [new([], groupId: "g1", cards: [new("target", 1, 3)]),
            new([new(Spell, 1, 3)], groupId: "g2"), new([new(Spell, 0, 0), new(A, 1, 3)], groupId: "g1")];
        Compare(cards, combos, 1);
        Compare(cards, combos, 3);
        Compare(cards, combos, 4);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MultipleDrawsPreservePriorityAndPayableCosts(bool customPriority) {
        List<Card> cards = [Rota(), Warrior(), new([B], id: "b"),
            new([], id: "draw1", drawCount: 1, preDrawRemovalCount: 1, preDrawSelector: CardSelector.For(Spell)),
            new([], id: "draw2", drawCount: 2), new([], id: "blank")];
        List<Combo> combos = [new([], groupId: "g1", cards: [new("target", 1, 4)]), new([new(B, 2, 3)], groupId: "g2")];
        Compare(cards, combos, 2, customPriority ? new(DrawActivationMode.CustomPriority, ["draw2", "draw1"]) : null);
    }

    [Test]
    public void FetchedDrawCardDoesNotRestartTheCompletedDrawPhase() {
        Card draw = new([A], id: "draw", drawCount: 2);
        Card searcher = new Card([], id: "source").WithSearch(new(CardSelector.For(A), false));
        Compare([searcher, draw, new([B], 2, id: "b"), new([], id: "blank")],
            [new([new(B, 2, 3)], groupId: "g1"), new([], cards: [new("draw", 1, 1)], groupId: "g2")], 1);
    }

    [Test]
    public void SeededOverlappingSearchesMatchEveryPhysicalTrajectory() {
        Random random = new(2048);
        for (int trial = 0; trial < 20; trial++) {
            Card source = new Card(random.Next(2) == 0 ? [A] : [B], id: "source")
                .WithSearch(new(CardSelector.For(A), false));
            List<Card> cards = [source, new Card([], id: "second").WithSearch(new(CardSelector.For(B), false))];
            for (int i = 0; i < 4; i++) {
                int bits = random.Next(4);
                cards.Add(new(new[] { A, B }.Where((_, bit) => (bits & (1 << bit)) != 0), id: $"target-{i}"));
            }
            List<Combo> combos = [new([new(A, 1, 1), new(B, 1, 3)], groupId: "g1"),
                new([new(A, 0, 0)], cards: [new("target-0", 1, 2)], groupId: "g2"),
                new([new(B, 2, 2)], groupId: "g1"), new([], cards: [new("source", 1, 1), new("target-1", 1, 1)])];
            Compare(cards, combos, 2);
        }
    }

    [Test]
    public void HiddenOperationsAreExplicitlyRejectedButOpeningHandOnlyStillRuns() {
        foreach (Card hidden in new[] { new Card([], drawCount: 1, preDrawDeckRemovalCount: 1),
            new Card([], drawCount: 1, postDrawDeckMillCount: 1) }) {
            List<Card> deck = [Rota(), Warrior(), hidden, new([], 3)];
            Assert.Throws<UnsupportedSearchInteractionException>(() => new ProbabilityCalculatorService()
                .CalculateProbabilityResults(deck, [new([new(A, 1, 3)])], 2));
            CalculationSnapshot off = CalculationSnapshot.Capture(deck, [new([new(A, 1, 3)])], 2, [], drawEffectsEnabled: false);
            Assert.DoesNotThrow(() => CalculationWire.ReadResult(CalculationWire.Execute(off.Json)));
            string response = CalculationWire.Execute(CalculationSnapshot.Capture(deck, [], 2, []).Json);
            Assert.That(response, Does.Contain("remaining target identities are unknown"));
        }
        Assert.Throws<ArgumentException>(() => new Card([], drawCount: 1, search: Rota().Search));
    }

    [Test]
    public void SearchWorkAndStorageAreBounded() {
        Assert.Throws<ProbabilityCalculationLimitException>(() => new ProbabilityCalculatorService().CalculateProbabilityResults(
            [Rota(2), Warrior(2), new([], 2)], [new([new(A, 2, 2)])], 2, null, new(1)));
        WorkBudget budget = new(CalculationWorkPolicy.Interactive, CalculationStoragePolicy.Standard);
        Assert.Throws<ProbabilityCalculationLimitException>(() => new PostDrawCalculation(
            [Rota(), Warrior(), new([], 3000)], 2, budget).Calculate([new([new(A, 1, 2)])], null));
    }

    [Test]
    public async Task BundledExampleHasOnlyRazenAndCompletesUnderStandard() {
        SessionState session = await LoadExample("search_example_session_state.json");
        Card rota = session.Cards.Single(card => card.ExternalCardId == 32807846);
        Card razen = session.Cards.Single(card => card.ExternalCardId == 29302858);
        Assert.Multiple(() => {
            Assert.That(session.Cards.Sum(card => card.Copies), Is.EqualTo(40));
            Assert.That(session.Cards.Where(rota.Search!.Matches).Select(card => card.Id), Is.EqualTo(new[] { razen.Id }));
            Assert.That(rota.Categories.Select(category => category.MetadataKey), Is.EquivalentTo(new[] { "kind:spell", "spell-type:normal" }));
            Assert.That(rota.ManualMetadataCategoryKeys, Is.Empty);
            Assert.That(rota.EffectPlan, Is.EqualTo(Rota().EffectPlan));
        });
        WorkBudget budget = new(CalculationWorkPolicy.Interactive, CalculationStoragePolicy.Standard);
        Stopwatch watch = Stopwatch.StartNew();
        ExactCalculationResult result = new DrawEffectCalculation(session.Cards, 5, budget).Calculate(session.Combos, session.ComboGroups);
        TestContext.Out.WriteLine($"Search example {watch.Elapsed.TotalMilliseconds:F2}ms; {budget.Spent} work; {budget.PeakCells} cells; exact {result.Total}; result {result.Total.ToDouble(budget):R}");
        // Independent enumeration of all C(40,5) physical hands, followed by the
        // only optional transfer. This also verifies genuine direct-ID access.
        List<Combo> objectives = [.. session.Combos, new([], cards: [new(razen.Id, 1, 5)])];
        BigInteger[] wins = new BigInteger[objectives.Count];
        Card[] physical = session.Cards.SelectMany(card => Enumerable.Repeat(card, card.Copies)).ToArray();
        Card[] hand = new Card[5];
        long count = 0;
        long totalWins = 0;
        long[] groupWins = new long[session.ComboGroups.Count];
        Func<IReadOnlyList<Card>, bool>[] predicates = objectives.Select(combo => SmallDeckOracle.HandPredicate(combo, 5)).ToArray();
        Visit(0, 0);
        for (int i = 0; i < session.Combos.Count; i++) {
            AssertRatio(result.Combos[i], wins[i], count);
        }
        Assert.That(count, Is.EqualTo(658008));
        AssertRatio(result.Total, totalWins, count);
        AssertRatio(result.Total, 550987, 658008);
        for (int i = 0; i < groupWins.Length; i++) {
            AssertRatio(result.Groups[i], groupWins[i], count);
        }
        Compare([rota.WithCopies(1), razen.WithCopies(2), new([], 2)], [objectives[^1]], 2);

        void Visit(int start, int depth) {
            if (depth == 5) {
                count++;
                Card[]? searched = hand.Contains(rota) && hand.Count(card => card == razen) < razen.Copies
                    ? hand.Select((card, index) => index == Array.IndexOf(hand, rota) ? razen : card).ToArray() : null;
                bool[] matched = new bool[session.Combos.Count];
                for (int i = 0; i < predicates.Length; i++) {
                    if (predicates[i](hand) || (searched is not null && predicates[i](searched))) {
                        wins[i]++;
                        if (i < matched.Length) {
                            matched[i] = true;
                        }
                    }
                }
                if (matched.Any(value => value)) {
                    totalWins++;
                }
                for (int i = 0; i < groupWins.Length; i++) {
                    if (session.Combos.Where((combo, index) => matched[index] && combo.GroupId == session.ComboGroups[i].Id).Any()) {
                        groupWins[i]++;
                    }
                }
                return;
            }
            for (int i = start; i <= physical.Length - (5 - depth); i++) {
                hand[depth] = physical[i];
                Visit(i + 1, depth + 1);
            }
        }
    }

    internal static async Task<SessionState> LoadExample(string filename) => await new SessionService(
        Mock.Of<IJSRuntime>(), new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer()).LoadSessionAsync(
            File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", filename)));

    private static void Compare(List<Card> cards, List<Combo> combos, int handSize, DrawActivationPriority? priority = null) {
        (BigInteger denominator, BigInteger[] wins) = SearchPhysicalOracle.Count(cards, combos, handSize, Groups, priority?.CardIds);
        WorkBudget budget = new(CalculationWorkPolicy.Interactive);
        ExactCalculationResult result = new DrawEffectCalculation(cards, handSize, budget, priority).Calculate(combos, Groups);
        ExactProbability[] rows = [result.Total, .. result.Combos, .. result.Groups];
        for (int i = 0; i < rows.Length; i++) {
            AssertRatio(rows[i], wins[i], denominator);
        }
        ProbabilityCalculationResult service = new ProbabilityCalculatorService().CalculateProbabilityResults(cards, combos, handSize, Groups,
            CalculationWorkPolicy.Interactive, options: new(priority));
        Assert.That(service.TotalProbability, Is.EqualTo(result.Total.ToDouble(budget)));
    }

    private static void AssertRatio(ExactProbability actual, BigInteger numerator, BigInteger denominator) =>
        Assert.That(actual.Numerator * denominator, Is.EqualTo(numerator * actual.Denominator));
}

// Independent labeled physical-copy oracle. Full permutations retain all visible
// draw trajectories, while the existing independent draw policy exposes the
// untouched suffix. Search options use physical list removals, no count vectors,
// memoization, production eligibility compiler, or Hall/event compiler.
internal static class SearchPhysicalOracle {
    internal static (BigInteger Total, BigInteger[] Wins) Count(List<Card> cards, List<Combo> combos, int size,
        IReadOnlyList<ComboGroup> groups, IReadOnlyList<string>? priority) {
        Card[] order = cards.Where(card => card.Active).SelectMany(card => Enumerable.Repeat(card, card.Copies)).ToArray();
        BigInteger total = 0;
        BigInteger[] wins = new BigInteger[1 + combos.Count + groups.Count];
        Permute(0);
        return (total, wins);

        void Permute(int depth) {
            if (depth == order.Length) {
                total++;
                List<Card> remaining = [];
                List<Card>? hand = DrawSequenceOracle.Resolve(order, size, combos, priority, rest => remaining = rest.ToList());
                if (hand is null) {
                    return;
                }
                bool[] matched = new bool[combos.Count];
                Search(hand, remaining, matched);
                bool[] results = [matched.Any(value => value), .. matched,
                    .. groups.Select(group => combos.Where((combo, i) => matched[i] && combo.GroupId == group.Id).Any())];
                for (int i = 0; i < results.Length; i++) {
                    if (results[i]) {
                        wins[i]++;
                    }
                }
                return;
            }
            for (int i = depth; i < order.Length; i++) {
                (order[i], order[depth]) = (order[depth], order[i]);
                Permute(depth + 1);
                (order[i], order[depth]) = (order[depth], order[i]);
            }
        }

        void Search(List<Card> hand, List<Card> remaining, bool[] matched) {
            for (int i = 0; i < combos.Count; i++) {
                matched[i] |= SmallDeckOracle.MatchesHand(hand, combos[i]);
            }
            for (int source = 0; source < hand.Count; source++) {
                if (hand[source].Search is not { } search) {
                    continue;
                }
                for (int target = 0; target < remaining.Count; target++) {
                    Card card = remaining[target];
                    Card inspected = search.ObjectiveMetadataOnly
                        ? new(card.Categories.Where(category => category.Source == CategorySource.Metadata
                            && !card.ManualMetadataCategoryKeys.Contains(category.MetadataKey!))) : card;
                    if ((search.ObjectiveMetadataOnly && card.ExternalCardId is not > 0)
                        || !DrawSequenceOracle.MatchesSelector(search.Selector, inspected)) {
                        continue;
                    }
                    List<Card> nextHand = [.. hand];
                    nextHand.RemoveAt(source);
                    nextHand.Add(card);
                    List<Card> nextDeck = [.. remaining];
                    nextDeck.RemoveAt(target);
                    Search(nextHand, nextDeck, matched);
                }
            }
        }
    }
}
