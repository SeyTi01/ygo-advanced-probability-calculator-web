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
public sealed partial class SearchEffectsTest {
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

    [TestCase("a-source", "z-blank")]
    [TestCase("z-source", "a-blank")]
    public void SearchSourceCannotBeAveragedWithDiscardableFiller(string sourceId, string fillerId) {
        List<Card> deck = [new Card([], id: sourceId).WithSearch(new(CardSelector.For(A), false)),
            new([A], id: "target"), new([], 2, id: fillerId), new([], id: "draw", drawCount: 1, postDrawDiscardCount: 1)];
        List<Combo> combos = [new([new(A, 1, 2)], groupId: "g1")];
        (BigInteger total, BigInteger[] wins) = SearchAccessOracle.Count(deck, combos, 2, Groups, null);
        Assert.That(total, Is.EqualTo(new BigInteger(120)));
        Assert.That(wins[0], Is.EqualTo(new BigInteger(100)));
        WorkBudget budget = new(CalculationWorkPolicy.Interactive);
        ExactCalculationResult result = new DrawEffectCalculation(deck, 2, budget).Calculate(combos, Groups);
        TestContext.Out.WriteLine($"Pooling counterexample {sourceId}: actual {result.Total.Numerator}/{result.Total.Denominator}; oracle {wins[0]}/{total}");
        Compare(deck, combos, 2);
        deck.Reverse();
        Compare(deck, combos, 2);
    }

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
    public void OverlappingSelectorsUseOnlyOriginallyHeldSources() {
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
    public void DrawThenSearchUsesSurvivingSourcesAndHandBoundedAccess(int removal) {
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
    public void SeededOverlappingSearchesMatchIndependentAssignments() {
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

    [TestCase(false)]
    [TestCase(true)]
    public void HiddenOperationsUseIdentityBlindAccessAndPhysicalCapacity(bool mill) {
        Card hidden = new([], 1, id: "draw", drawCount: 1,
            preDrawDeckRemovalCount: mill ? 0 : 1, postDrawDeckMillCount: mill ? 1 : 0);
        List<Card> deck = [Rota(), Warrior(), hidden, new([], 2, id: "blank")];
        List<Combo> combos = [new([new(A, 1, 3)], groupId: "g1"), new([new(Spell, 1, 3)], groupId: "g2")];
        Compare(deck, combos, 2);
        Compare(deck, combos, 3);
        CalculationSnapshot off = CalculationSnapshot.Capture(deck, combos, 2, [], drawEffectsEnabled: false);
        Assert.DoesNotThrow(() => CalculationWire.ReadResult(CalculationWire.Execute(off.Json)));
        Assert.DoesNotThrow(() => CalculationWire.ReadResult(CalculationWire.Execute(CalculationSnapshot.Capture(deck, combos, 2, []).Json)));
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
        SessionState session = await LoadExample("rota_search_example_session_state.json");
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

    [TestCase(1, 0.8629362147164005)]
    [TestCase(3, 0.9089511612294494)]
    public async Task RepresentativeMixedSearchKeepsCountStateAndStandardCapacity(int copies, double expected) {
        SessionState session = await LoadExample("rota_search_example_session_state.json");
        int row = session.Cards.FindIndex(card => card.Name == "Mulcharmy Fuwalos");
        Card original = session.Cards[row];
        session.Cards[row] = original.WithCopies(copies).WithDrawCount(2).WithDrawOncePerTurn(false);
        if (copies < original.Copies) {
            session.Cards.Add(original.Duplicate().WithCopies(original.Copies - copies));
        }
        WorkBudget budget = new(CalculationWorkPolicy.Interactive, CalculationStoragePolicy.Standard);
        ExactCalculationResult result = new DrawEffectCalculation(session.Cards, 5, budget).Calculate(session.Combos, session.ComboGroups);
        Assert.That(result.Total.ToDouble(budget), Is.EqualTo(expected));
        Assert.That(budget.PeakCells, Is.LessThan(10_000));
        Assert.That(CalculationWire.ReadResult(CalculationWire.Execute(CalculationSnapshot.Capture(session.Cards,
            session.Combos, 5, session.ComboGroups).Json)).TotalProbability, Is.EqualTo(expected));
    }

    [Test]
    public void AccessCapacityIsScalarAndFetchedSearchersDoNotActivate() {
        List<Card> deck = [Rota(), Warrior(), new([], id: "blank")];
        List<Combo> combos = [new([], cards: [new("target", 1, 1)])];
        foreach (int capacity in new[] { 0, 1 }) {
            WorkBudget budget = new(CalculationWorkPolicy.Interactive);
            ExactCalculationResult result = new SearchProbabilityCalculation([], 0, [deck[0]],
                new(deck, budget), capacity, budget).Calculate(combos, [], false);
            AssertRatio(result.Total, capacity, 1);
        }
        Card first = new Card([], id: "first").WithSearch(new(CardSelector.For(A), false));
        Card second = new Card([A], id: "second").WithSearch(new(CardSelector.For(B), false));
        List<Card> chain = [first, second, new([B], id: "end"), new([], id: "blank")];
        List<Combo> objectives = [new([], cards: [new("end", 1, 1)])];
        Compare(chain, objectives, 1);
        Assert.That(new ProbabilityCalculatorService().CalculateProbabilityResults(chain, objectives, 1).TotalProbability,
            Is.EqualTo(0.5), "Only the original second searcher or the target itself succeeds; a fetched second searcher stays in hand.");
    }

    [Test]
    public void FilteredDrawCostsKeepUnremovableSearchSourcesExplicit() {
        List<Card> deck = [Rota(), Warrior(), new([], id: "draw", drawCount: 1,
            preDrawRemovalCount: 1, preDrawSelector: CardSelector.For(B)), Warrior(id: "second-target"), new([], 2, id: "blank")];
        List<Combo> combos = [new([new(A, 1, 2)], groupId: "g1"), new([new(Spell, 1, 2)], groupId: "g2")];
        WorkBudget budget = new(CalculationWorkPolicy.Interactive);
        PostDrawCalculation calculation = new(deck, 2, budget);
        calculation.CalculateSearch(combos, Groups, false, new(deck, budget));
        // The old equality with no-search row counts was unsound: ROTA is an
        // observable source even when a filtered cost cannot remove it.
        Assert.That(calculation.DeferredRows, Is.EqualTo(0));
        Compare(deck, combos, 2);
    }

    [Test]
    public void AbstractFinalPoolUsesItsConditionalSubsetDistribution() {
        Card source = Rota();
        Card target = Warrior(2);
        Card blank = new([], id: "blank");
        List<Card> deck = [source, target, blank];
        List<Combo> combos = [new([new(A, 2, 2)], groupId: "g1")];
        WorkBudget budget = new(CalculationWorkPolicy.Interactive);
        // This counter-level input is valid only for a genuinely uniform pool,
        // e.g. an opening sample with no identity-sensitive removal. The draw
        // reducer must never manufacture this pool after discarding its source.
        ExactCalculationResult result = new SearchProbabilityCalculation([source, blank], 1, [target.WithCopies(1)],
            new(deck, budget), 1, budget, [new([source, blank], 1)]).Calculate(combos, Groups, false);
        AssertRatio(result.Total, 1, 2);
        AssertRatio(result.Combos[0], 1, 2);
        AssertRatio(result.Groups[0], 1, 2);
        AssertRatio(result.Groups[1], 0, 1);
    }

    [Test]
    public void MultiplicityCopiesKeepIndependentCategoriesAndImmutableSearchProvenance() {
        Card source = Rota();
        Card copy = source.WithCopies(2);
        copy.Categories.Add(A);
        Assert.Multiple(() => {
            Assert.That(source.Categories, Does.Not.Contain(A));
            Assert.That(copy.Copies, Is.EqualTo(2));
            Assert.That(copy.Id, Is.EqualTo(source.Id));
            Assert.That(copy.EffectPlan, Is.EqualTo(source.EffectPlan));
            Assert.That(copy.EffectProfile, Is.EqualTo(source.EffectProfile));
            Assert.That(copy.ManualMetadataCategoryKeys, Is.EqualTo(source.ManualMetadataCategoryKeys));
        });
    }

    internal static async Task<SessionState> LoadExample(string filename) => await new SessionService(
        Mock.Of<IJSRuntime>(), new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer()).LoadSessionAsync(
            File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", filename)));

    private static void Compare(List<Card> cards, List<Combo> combos, int handSize, DrawActivationPriority? priority = null) {
        (BigInteger denominator, BigInteger[] wins) = SearchAccessOracle.Count(cards, combos, handSize, Groups, priority?.CardIds);
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

// Independent labeled-copy reference for the declared access model. The draw
// oracle supplies its actual final hand and remaining physical QUANTITY. Target
// identities are deliberately reset to original copies minus that hand, matching
// the documented mixed-draw approximation. Slot-by-slot substitutions cannot
// activate a fetched effect. No production event compiler or count shifts are used.
internal static class SearchAccessOracle {
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
                List<Card> available = cards.Where(card => card.Active).SelectMany(card => Enumerable.Repeat(card, card.Copies)).ToList();
                foreach (Card held in hand) {
                    available.Remove(held);
                }
                Search(hand, available, matched, 0, remaining.Count, []);
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

        void Search(List<Card> hand, List<Card> remaining, bool[] matched, int slot, int capacity, List<Card> activated) {
            if (slot == hand.Count) {
                for (int i = 0; i < combos.Count; i++) {
                    matched[i] |= SmallDeckOracle.MatchesHand(hand, combos[i]);
                }
                return;
            }
            Search(hand, remaining, matched, slot + 1, capacity, activated);
            if (capacity == 0 || hand[slot].Search is not { } search) {
                return;
            }
            Card source = hand[slot];
            if (source.OncePerTurn && activated.Any(previous => previous.Id == source.Id)) {
                return;
            }
            for (int target = 0; target < remaining.Count; target++) {
                Card card = remaining[target];
                if (card.Id == source.Id) {
                    continue;
                }
                Card inspected = search.ObjectiveMetadataOnly
                    ? new(card.Categories.Where(category => category.Source == CategorySource.Metadata
                        && !card.ManualMetadataCategoryKeys.Contains(category.MetadataKey!)), externalCardId: card.ExternalCardId) : card;
                if ((search.ObjectiveMetadataOnly && card.ExternalCardId is not > 0)
                    || !DrawSequenceOracle.MatchesSelector(search.Selector, inspected)) {
                    continue;
                }
                List<Card> nextHand = [.. hand];
                nextHand[slot] = card;
                List<Card> nextDeck = [.. remaining];
                nextDeck.RemoveAt(target);
                Search(nextHand, nextDeck, matched, slot + 1, capacity - 1, [.. activated, source]);
            }
        }
    }
}
