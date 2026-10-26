using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public sealed class TwoPhaseSearchPriorityTest {
    private static readonly CategoryBase A = new("Oracle A");
    private static readonly CategoryBase B = new("Oracle B");
    private static readonly CategoryBase Cost = new("Oracle cost");
    private static readonly CategoryBase DrawSource = new("Oracle draw source");

    [TestCase(true, 1, 2)]
    [TestCase(false, 1, 2)]
    public void CrossPhaseRanksAlwaysResolveDrawsBeforeSearches(bool searchFirst, int numerator, int denominator) {
        List<Card> deck = [Search("s", CardSelector.AnyOfSelectors([CardSelector.For(A), CardSelector.For(B)])),
            new([], id: "d", drawCount: 1), new([A], id: "a"), new([B], id: "b"), new([], id: "c")];
        List<Combo> combos = [new([], groupId: "g", cards: [new("a", 1, 1), new("b", 1, 1)])];
        DrawActivationPriority priority = Custom(searchFirst ? ["s", "d"] : ["d", "s"]);
        AssertOracle(deck, 2, combos, [new("g", "Both")], priority,
            [(numerator, denominator), (numerator, denominator), (numerator, denominator)], checkWire: true);
    }

    [TestCase(true, 3, 5)]
    [TestCase(false, 7, 10)]
    public void SearchOnlyPriorityControlsHandCostsAndLaterMinimumHandSize(bool costlyFirst, int numerator, int denominator) {
        List<Card> deck = [new([], id: "s", search: new(CardSelector.For(A), false),
                preDrawRemovalCount: 1, preDrawSelector: CardSelector.For(Cost)),
            new([], id: "t", search: new(CardSelector.For(B), false), minimumHandSize: 3),
            new([Cost], id: "cost"), new([A], id: "a"), new([B], id: "b")];
        List<Combo> combos = [new([], cards: [new("a", 1, 1), new("b", 1, 1)])];
        AssertOracle(deck, 3, combos, [], Custom(costlyFirst ? ["s", "t"] : ["t", "s"]),
            [(numerator, denominator), (numerator, denominator)]);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SearchPriorityChangesFinalFeasibilityAfterTheDrawPhase(bool hiddenLoss) {
        List<Card> deck = [new([], id: "s", search: new(CardSelector.For(A), false),
                preDrawRemovalCount: 1, preDrawSelector: CardSelector.For(Cost)),
            new([], id: "t", search: new(CardSelector.For(B), false), minimumHandSize: 3),
            new([Cost], id: "cost"), new([A], id: "a"), new([B], id: "b"),
            new([], id: "draw", drawCount: 1, preDrawDeckRemovalCount: hiddenLoss ? 1 : 0), new([], id: "f")];
        List<Combo> combos = [new([], groupId: "g", cards: [new("a", 1, 1), new("b", 1, 1)]),
            new([new(A, 0, 0)], groupId: "g")];
        List<ComboGroup> groups = [new("g", "Union"), new("empty", "Empty")];
        DrawActivationPriority paid = Custom("s", "draw", "t");
        DrawActivationPriority minimum = Custom("t", "draw", "s");
        AssertOracle(deck, 3, combos, groups, paid, checkWire: true);
        AssertOracle(deck, 3, combos, groups, minimum);
        TwoPhaseSearchOracle.Rational first = TwoPhaseSearchOracle.Count(deck, 3, combos, groups, paid)[1];
        TwoPhaseSearchOracle.Rational second = TwoPhaseSearchOracle.Count(deck, 3, combos, groups, minimum)[1];
        Assert.That(first.Numerator * second.Denominator, Is.LessThan(second.Numerator * first.Denominator));
        TestContext.Out.WriteLine($"hidden={hiddenLoss}: paid first {first}; minimum first {second}");
    }

    [Test]
    public void UnrankedSearchSourcesKeepPhysicalEncounterTiesRegardlessOfDeckSorting() {
        List<Card> deck = [new([], id: "s", search: new(CardSelector.For(A), false),
                preDrawRemovalCount: 1, preDrawSelector: CardSelector.For(Cost)),
            new([], id: "t", search: new(CardSelector.For(B), false), minimumHandSize: 3),
            new([Cost], id: "cost"), new([A], id: "a"), new([B], id: "b")];
        List<Combo> combos = [new([], cards: [new("a", 1, 1), new("b", 1, 1)])];
        AssertOracle(deck, 3, combos, [], DrawActivationPriority.Default, [(13, 20), (13, 20)]);
        deck.Reverse();
        AssertOracle(deck, 3, combos, [], Custom("missing"), [(13, 20), (13, 20)]);
        deck.Add(new([], id: "draw", drawCount: 1));
        AssertOracle(deck, 3, combos, [], DrawActivationPriority.Default);
    }

    [Test]
    public void OrdinaryAndDrawOnlyControlsMatchTheLabeledOracle() {
        List<Card> deck = [new([A, B], id: "ab"), new([A], id: "a"), new([B], id: "b"), new([], id: "f")];
        List<Combo> combos = [new([new(A, 1, 2), new(B, 1, 2)], groupId: "g"), new([new(A, 0, 0)])];
        AssertOracle(deck, 2, combos, [new("g", "Union")], DrawActivationPriority.Default);
        deck.Add(new([], id: "draw", drawCount: 1));
        AssertOracle(deck, 2, combos, [new("g", "Union")], DrawActivationPriority.Default);
    }

    [Test]
    public void EachComboAndEachUnionUsesBooleanFinalHandFeasibility() {
        List<Card> deck = [Search("s", CardSelector.AnyOfSelectors([CardSelector.For(A), CardSelector.For(B)])),
            new([], id: "d", drawCount: 1), new([A], id: "a"), new([B], id: "b"),
            new([], id: "c"), new([], id: "e")];
        List<Combo> combos = [
            new([], groupId: "g", cards: [new("a", 1, 1), new("c", 1, 1)]),
            new([], groupId: "g", cards: [new("b", 1, 1), new("e", 1, 1)])
        ];
        AssertOracle(deck, 2, combos, [new("g", "Union"), new("empty", "Empty")], Custom("s", "d"),
            null);
    }

    [TestCase(false, 1, 3)]
    [TestCase(true, 2, 3)]
    public void DrawnSourcesBecomeEligibleAndFetchedSourcesStayInert(bool fetchOrdinaryTarget, int numerator, int denominator) {
        List<Card> deck = [Search("s", CardSelector.For(fetchOrdinaryTarget ? A : DrawSource)),
            new([DrawSource], id: "d", drawCount: 1), new([A], id: "a"), new([], id: "c")];
        AssertOracle(deck, 1, [new([], cards: [new("a", 1, 1)])], [], Custom("s", "d"),
            [(numerator, denominator), (numerator, denominator)]);
    }

    [TestCase(true, false, 1, 2)]
    [TestCase(false, false, 3, 5)]
    [TestCase(true, true, 3, 5)]
    public void ActivationAllowancesUseEntryIdsAndDistinctCopies(bool oncePerEntry, bool separateEntries, int numerator, int denominator) {
        Card first = new([], separateEntries ? 1 : 2, "Same display name", id: "s", search: new(CardSelector.For(A), false),
            oncePerTurn: oncePerEntry);
        List<Card> deck = [first, new([A], 2, "Same display name", id: "a"), new([], id: "c")];
        if (separateEntries) {
            deck.Add(new([], name: "Same display name", id: "t", search: new(CardSelector.For(A), false), oncePerTurn: oncePerEntry));
        }
        AssertOracle(deck, 2, [new([], cards: [new("a", 2, 2)])], [], Custom("s", "t"),
            [(numerator, denominator), (numerator, denominator)]);
    }

    [Test]
    public void SearchModesShareEntryAllowanceButRetainTheirOwnCaps() {
        CardSelector a = CardSelector.For(A);
        CardSelector b = CardSelector.For(B);
        SearchEffectPlan search = new(CardSelector.AnyOfSelectors([a, b]), false, [new(a, 1), new(b, 1)]);
        List<Card> deck = [new([], 2, id: "s", search: search, activationLimit: 2),
            new([A], 2, id: "a"), new([B], id: "b"), new([], id: "c")];
        AssertOracle(deck, 2, [new([], cards: [new("a", 2, 2)])], [], Custom("s"), [(1, 3), (1, 3)]);
        AssertOracle(deck, 2, [new([], cards: [new("a", 1, 1), new("b", 1, 1)])], [], Custom("s"), [(3, 5), (3, 5)]);
    }

    [Test]
    public void MandatoryRemovalFinishesBeforeDrawnSearcherAndKeepsFailedMass() {
        List<Card> deck = [Search("s", CardSelector.For(A)),
            new([], id: "d", drawCount: 1, postDrawDiscardCount: 1, postDrawSelector: CardSelector.For(A)),
            new([A], id: "a"), new([], id: "c")];
        List<Combo> combos = [new([], groupId: "g", cards: [new("a", 1, 1)]), new([], groupId: "g")];
        AssertOracle(deck, 1, combos, [new("g", "Surviving outcomes")], Custom("s", "d"),
            [(5, 6), (1, 2), (5, 6), (5, 6)]);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SearchObservesSurvivingTargetsAfterHiddenLossWithoutObservingFutureOrder(bool mill) {
        List<Card> deck = [Search("s", CardSelector.For(A)),
            new([], id: "d", drawCount: 1, preDrawDeckRemovalCount: mill ? 0 : 1, postDrawDeckMillCount: mill ? 1 : 0),
            new([A], id: "a"), new([], id: "c"), new([], id: "e")];
        AssertOracle(deck, 1, [new([], cards: [new("a", 1, 1)])], [], Custom("s", "d"), null);
    }

    [Test]
    public void SearchChoicesUseJointSurvivorsAfterAllDraws() {
        List<Card> deck = [new([], id: "h", drawCount: 1, preDrawDeckRemovalCount: 1),
            Search("s", CardSelector.AnyOfSelectors([CardSelector.For(A), CardSelector.For(B)])),
            new([], id: "d", drawCount: 1), new([A], id: "a"), new([B], id: "b"),
            new([], id: "c"), new([], id: "e")];
        List<Combo> combos = [
            new([], groupId: "g", cards: [new("a", 1, 1), new("c", 1, 1)]),
            new([], groupId: "g", cards: [new("b", 1, 1), new("e", 1, 1)])
        ];
        AssertOracle(deck, 3, combos, [new("g", "Union")], Custom("h", "s", "d"),
            null);
    }

    [Test]
    public void PriorityTiesAndDeckSortingPreserveThePhysicalEncounterPolicy() {
        List<Card> deck = [Search("s", CardSelector.AnyOfSelectors([CardSelector.For(A), CardSelector.For(B)])),
            new([], id: "d", drawCount: 1), new([A], id: "a"), new([B], id: "b"), new([], id: "c")];
        List<Combo> combos = [new([], cards: [new("a", 1, 1), new("b", 1, 1)])];
        foreach (DrawActivationPriority priority in new[] { DrawActivationPriority.Default, Custom("missing") }) {
            AssertOracle(deck, 2, combos, [], priority, [(1, 2), (1, 2)]);
        }
        deck.Reverse();
        AssertOracle(deck, 2, combos, [], Custom("s", "d"), null);
    }

    [Test]
    public void DisabledEffectsRetainTheirPhysicalCardsAndDormantRanking() {
        List<Card> deck = [Search("s", CardSelector.For(A)).WithSimulationEnabled(false),
            new([], id: "d", drawCount: 1), new([A], id: "a"), new([], id: "c"),
            new([], 2, active: false, id: "inactive", search: new(CardSelector.For(A), false)),
            new([], 0, id: "zero", search: new(CardSelector.For(A), false))];
        AssertOracle(deck, 1, [new([], cards: [new("a", 1, 1)])], [], Custom("inactive", "zero", "s", "d"),
            [(1, 3), (1, 3)], checkWire: true);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void DirectAndOverlappingCategoryObjectivesPreserveDistinctCopiesAndZeroMaxima(bool searchFirst) {
        List<Card> deck = [Search("s", CardSelector.AnyOfSelectors([CardSelector.For(A), CardSelector.For(B)])),
            new([], id: "d", drawCount: 1), new([A, B], id: "ab"), new([A], id: "a"),
            new([B], id: "b"), new([], id: "c")];
        List<Combo> combos = [
            new([new(A, 1, 2), new(B, 1, 2)], groupId: "g"),
            new([new(A, 0, 0), new(B, 1, 2)], groupId: "g"),
            new([new(A, 1, 2)], cards: [new("ab", 1, 1)]),
            new([new(A, 0, 0)], cards: [new("s", 1, 1)])
        ];
        AssertOracle(deck, 2, combos, [new("g", "Category union")], Custom(searchFirst ? ["s", "d"] : ["d", "s"]));
    }

    [Test]
    public void SearchCannotReopenADrawWhoseCostWasUnavailable() {
        List<Card> deck = [Search("s", CardSelector.For(Cost)),
            new([], id: "d", drawCount: 2, preDrawRemovalCount: 1, preDrawSelector: CardSelector.For(Cost)),
            new([Cost], id: "cost"), new([A], id: "a"), new([B], id: "b"), new([], id: "c")];
        List<Combo> combos = [new([], groupId: "g", cards: [new("a", 1, 1), new("b", 1, 1)]),
            new([], groupId: "g", cards: [new("d", 1, 1)]), new([new(Cost, 0, 0), new(A, 1, 2)])];
        AssertOracle(deck, 2, combos, [new("g", "Cost-sensitive union")], Custom("d", "s"));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void SearchDoesNotUseTheFirstDrawAllowanceAndDrawLocksDoNotBlockSearch(bool lockFirst) {
        List<Card> deck = [Search("s", CardSelector.For(A)),
            new([], id: "first", drawCount: 1, firstActivationOnly: true),
            new([], id: "lock", drawCount: 1, blocksLaterDraws: true),
            new([A], id: "a"), new([B], id: "b"), new([], id: "c")];
        List<Combo> combos = [new([], groupId: "g", cards: [new("first", 1, 1), new("a", 1, 1)]),
            new([new(B, 1, 2)], groupId: "g"), new([new(A, 0, 0)])];
        AssertOracle(deck, 2, combos, [new("g", "Timing union")],
            Custom(lockFirst ? ["lock", "s", "first"] : ["s", "first", "lock"]));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void PhysicalCopiesRespectSharedEntryAndModeLimitsWithinSearchPhase(bool searchFirst) {
        CardSelector a = CardSelector.For(A);
        CardSelector b = CardSelector.For(B);
        List<Card> deck = [new([], 2, name: "Same name", id: "s",
                search: new(CardSelector.AnyOfSelectors([a, b]), false, [new(a, 1), new(b, 1)]), activationLimit: 2),
            new([], 2, name: "Same name", id: "d", drawCount: 1, oncePerTurn: false),
            new([A], id: "a"), new([B], id: "b"), new([], id: "c")];
        List<Combo> combos = [new([], groupId: "g", cards: [new("a", 1, 1), new("b", 1, 1)]),
            new([new(A, 0, 0)], groupId: "g", cards: [new("d", 1, 1)]),
            new([], cards: [new("s", 1, 1)])];
        AssertOracle(deck, 2, combos, [new("g", "Copy union")], Custom(searchFirst ? ["s", "d"] : ["d", "s"]));
    }

    [Test]
    public void InspectingMissingTargetsDoesNotChangeTheFixedDiscardPolicy() {
        List<Card> deck = [new([], id: "h", drawCount: 1, preDrawDeckRemovalCount: 1),
            Search("z-source", CardSelector.For(B)), new([], id: "d", drawCount: 1, preDrawRemovalCount: 1),
            new([A], id: "f-piece"), new([B], id: "target"), new([], 2, id: "a-filler")];
        List<Combo> combos = [new([], groupId: "g", cards: [new("f-piece", 1, 1)]),
            new([], groupId: "g", cards: [new("target", 1, 1)])];
        // Witness: open H,S,D; H hides the only target and draws F. S cannot
        // search, but its plausible positive target still protects it in the
        // fixed removal policy. D pays F, then draws filler. Retiring S after
        // inspection would instead discard S and incorrectly preserve F.
        AssertOracle(deck, 3, combos, [new("g", "Union")], Custom("h", "z-source", "d"),
            null);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AmbiguousHandPaymentsAndDiscardsFollowTheFixedPolicyAcrossSearchChoices(bool handCost) {
        List<Card> deck = [Search("s", CardSelector.AnyOfSelectors([CardSelector.For(A), CardSelector.For(B)])),
            new([], id: "d", drawCount: 1, preDrawRemovalCount: handCost ? 1 : 0,
                postDrawDiscardCount: handCost ? 0 : 1),
            new([A], id: "a"), new([B], id: "b"), new([A, B], id: "ab"), new([], id: "f")];
        List<Combo> combos = [new([new(A, 1, 2), new(B, 1, 2)], groupId: "g"),
            new([new(A, 0, 0)], groupId: "g"), new([], cards: [new("s", 1, 1)])];
        AssertOracle(deck, 3, combos, [new("g", "Union")], Custom("d", "s"));
        AssertOracle(deck, 3, combos, [new("g", "Union")], Custom("s", "d"));
    }

    [Test]
    public void SeededSmallDecksMatchIndependentTwoPhasePoliciesForEveryResultRow() {
        Random random = new(281);
        for (int fixture = 0; fixture < 6; fixture++) {
            CardSelector selector = fixture % 2 == 0 ? CardSelector.For(A)
                : CardSelector.AnyOfSelectors([CardSelector.For(A), CardSelector.For(B)]);
            List<Card> deck = [new([], random.Next(1, 3), id: "s", search: new(selector, false),
                    oncePerTurn: fixture % 2 == 0),
                new([], id: "d", drawCount: random.Next(1, 3), firstActivationOnly: fixture == 2,
                    blocksLaterDraws: fixture == 3, minimumHandSize: fixture == 4 ? 2 : 0),
                new([A], id: "a"), new([B], id: "b"), new(fixture % 2 == 0 ? [A, B] : [], id: "other")];
            List<Combo> combos = [new([new(A, 1, 2), new(B, 1, 2)], groupId: "g"),
                new([new(A, 0, 0)], groupId: "g", cards: [new("s", 1, 2)]),
                new([], cards: [new("a", 1, 1), new("d", 0, 0)])];
            DrawActivationPriority priority = (fixture % 3) switch {
                0 => DrawActivationPriority.Default,
                1 => Custom("s", "d"),
                _ => Custom("d", "s")
            };
            AssertOracle(deck, 2, combos, [new("g", "Seeded union")], priority);
        }
    }

    private static Card Search(string id, CardSelector selector) => new([], id: id, search: new(selector, false));
    private static DrawActivationPriority Custom(params string[] ids) => new(DrawActivationMode.CustomPriority, ids);

    private static void AssertOracle(List<Card> deck, int handSize, List<Combo> combos, List<ComboGroup> groups,
        DrawActivationPriority priority, (int Numerator, int Denominator)[]? expected = null, bool checkWire = false) {
        TwoPhaseSearchOracle.Rational[] oracle = TwoPhaseSearchOracle.Count(deck, handSize, combos, groups, priority);
        if (expected is not null) {
            Assert.That(oracle, Has.Length.EqualTo(expected.Length));
            for (int row = 0; row < oracle.Length; row++) {
                Assert.That(oracle[row].Numerator * expected[row].Denominator,
                    Is.EqualTo(expected[row].Numerator * oracle[row].Denominator), "Independent physical oracle row " + row);
            }
        }
        ProbabilityCalculatorService service = new();
        foreach (CalculationModel mode in new[] { CalculationModel.Exact, CalculationModel.Fast }) {
            CalculationOptions options = new(priority, Mode: mode);
            ProbabilityCalculationResult result = service.CalculateProbabilityResults(deck.Where(card => card.Active && card.Copies > 0).ToList(), combos, handSize, groups,
                CalculationWorkPolicy.Interactive, options: options);
            if (result.Contract!.Accuracy != CalculationAccuracy.Exact) {
                Assert.That(mode, Is.EqualTo(CalculationModel.Fast));
                Assert.That(result.SearchAvailability, Is.Not.Null);
                SearchAvailabilityInterval[] intervals = [result.SearchAvailability!.Total!,
                    .. result.SearchAvailability.Combos, .. result.SearchAvailability.Groups];
                for (int row = 0; row < oracle.Length; row++) {
                    Assert.That(BigInteger.Parse(intervals[row].Lower.Numerator) * oracle[row].Denominator,
                        Is.LessThanOrEqualTo(oracle[row].Numerator * BigInteger.Parse(intervals[row].Lower.Denominator)));
                    Assert.That(BigInteger.Parse(intervals[row].Upper.Numerator) * oracle[row].Denominator,
                        Is.GreaterThanOrEqualTo(oracle[row].Numerator * BigInteger.Parse(intervals[row].Upper.Denominator)));
                }
                continue;
            }
            ProbabilityFraction[] rows = [result.Contract.Fractions!.Total, .. result.Contract.Fractions.Combos,
                .. result.Contract.Fractions.Groups];
            Assert.That(rows, Has.Length.EqualTo(oracle.Length));
            for (int row = 0; row < rows.Length; row++) {
                Assert.That(BigInteger.Parse(rows[row].Numerator) * oracle[row].Denominator,
                    Is.EqualTo(oracle[row].Numerator * BigInteger.Parse(rows[row].Denominator)), mode + " row " + row);
            }
            if (checkWire) {
                CalculationSnapshot snapshot = CalculationSnapshot.Capture(deck, combos, handSize, groups, options: options);
                ProbabilityCalculationResult wire = CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json), mode);
                Assert.That(wire.Contract!.Fractions!.Total, Is.EqualTo(result.Contract.Fractions.Total));
                Assert.That(wire.Contract.Fractions.Combos, Is.EqualTo(result.Contract.Fractions.Combos));
                Assert.That(wire.Contract.Fractions.Groups, Is.EqualTo(result.Contract.Fractions.Groups));
            }
        }
    }
}
