using System.Numerics;
using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture, NonParallelizable]
internal sealed class CalculationCapabilityTest {
    private static readonly CategoryBase A = new("Target");
    private static readonly CategoryBase B = new("Other role");
    private static readonly CategoryBase Material = new("Material");
    private static readonly List<ComboGroup> Groups = [new("g", "Positive and negative"), new("empty", "Empty")];
    private static readonly List<Combo> Combos = [
        new([new(A, 1, 6)], "A", groupId: "g"),
        new([new(A, 0, 0)], "No A", groupId: "g"),
        new([new(A, 0, 1)], "At most one A"),
        new([new(A, 1, 6), new(B, 1, 6)], "Distinct A+B", groupId: "g"),
        new([], "No draw source", cards: [new("d", 0, 0)])
    ];

    internal static List<Card> OrderedDeck() => [
        new([], 1, id: "s", search: new(CardSelector.For(A), false)),
        new([A], 1, id: "a"),
        new([], 1, id: "d", drawCount: 1, firstActivationOnly: true, blocksLaterDraws: true),
        new([], 1, id: "e", drawCount: 1),
        new([], 1, id: "f")
    ];

    [TestCase(false), TestCase(true)]
    public void OrderedNoLossPublicRowsMatchPhysicalOracle(bool customPriority) {
        List<Card> deck = OrderedDeck();
        string[]? priority = customPriority ? ["e", "d"] : null;
        ApplicationResultFractions result = CheckSearch(deck, 2, priority);
        Assert.That(result.Combos[0].Numerator * 10, Is.EqualTo(result.Combos[0].Denominator * 9));
    }

    [TestCase(false, false), TestCase(true, false), TestCase(false, true), TestCase(true, true)]
    public void FilteredHandOperationsExcludeAllTargetsEvenWithRetryAndFailure(bool cost, bool duplicateNames) {
        List<Card> deck = [
            new([], 1, "same", id: "s", search: new(CardSelector.For(A), false)),
            new([A], 1, "same", id: "a"), new([B], 1, id: "b"), new([Material], 1, id: "m"),
            new([], 1, duplicateNames ? "same" : "draw", id: "d", drawCount: 1,
                preDrawRemovalCount: cost ? 1 : 0, preDrawSelector: cost ? CardSelector.For(Material) : null,
                postDrawDiscardCount: cost ? 0 : 1, postDrawSelector: cost ? null : CardSelector.For(Material)),
            new([], 1, id: "e", drawCount: 1)
        ];
        // d may initially be unpayable, then retry after e draws material.
        CheckSearch(deck, 2, ["d", "e"]);
    }

    [TestCase("ordinary")]
    [TestCase("inert")]
    [TestCase("count")]
    [TestCase("single-opt-lock")]
    [TestCase("ordered")]
    [TestCase("exchangeable")]
    [TestCase("deferred")]
    public void NoSearchModelsMatchIndependentPhysicalRows(string kind) {
        List<Card> deck = [new([A], 1, id: "a"), new([B], 1, id: "b"), new([Material], 1, id: "m"), new([], 2, id: "f")];
        if (kind != "ordinary") {
            deck.Add(new([], 1, id: "d", drawCount: kind is "inert" or "exchangeable" ? 2 : 1,
                drawOncePerTurn: kind != "exchangeable", preDrawDeckRemovalCount: kind == "inert" ? 10 : 0,
                postDrawDiscardCount: kind is "exchangeable" or "deferred" ? 1 : 0,
                postDrawSelector: kind == "deferred" ? CardSelector.For(Material) : null,
                firstActivationOnly: kind is "ordered" or "single-opt-lock", blocksLaterDraws: kind is "ordered" or "single-opt-lock"));
        }
        if (kind == "ordered") {
            deck.Add(new([], 1, id: "e", drawCount: 1));
        }
        ApplicationResultFractions result = ApplicationResultFractions.From(new ProbabilityCalculatorService().CalculateProbabilityResults(
            deck, Combos, 2, Groups, CalculationWorkPolicy.Default, new(Mode: CalculationModel.Exact)));
        DrawSequenceOracle.Counts oracle = DrawSequenceOracle.Enumerate(deck, 2, Combos, Groups);
        AssertRows(result, oracle.Wins, oracle.Total);
    }

    [TestCase("consumed", 1, 2, 1, 3)]
    [TestCase("discard", 17, 30, 1, 2)]
    [TestCase("banish", 5, 6, 23, 30)]
    [TestCase("mill", 5, 6, 23, 30)]
    public void TargetLossProjectionAndExactModeMatchTheirIndependentOracles(
        string kind,
        int optimisticN,
        int optimisticD,
        int physicalN,
        int physicalD
    ) {
        List<Card> deck = kind == "consumed"
            ? [new([], 1, id: "s", search: new(CardSelector.For(A), false)), new([A], 1, id: "d", drawCount: 1), new([], 1, id: "f")]
            : [new([], 1, id: "s", search: new(CardSelector.For(A), false)), new([A], 1, id: "a"), new([], 2, id: "f"),
                new([], 1, id: "d", drawCount: 1, preDrawDeckRemovalCount: kind == "banish" ? 1 : 0,
                    postDrawDeckMillCount: kind == "mill" ? 1 : 0, postDrawDiscardCount: kind == "discard" ? 1 : 0,
                    postDrawSelector: kind == "discard" ? CardSelector.For(A) : null)];
        int hand = kind == "consumed" ? 1 : 2;
        ApplicationResultFractions result = CheckSearch(deck, hand);
        SearchCertificateOracle.Counts physical = SearchCertificateOracle.Enumerate(deck, hand, Combos, Groups);
        Assert.That(result.Combos[0].Numerator * optimisticD, Is.EqualTo(result.Combos[0].Denominator * optimisticN));
        Assert.That(physical.Wins[1][1] * physicalD, Is.EqualTo(physical.Outcomes * physicalN));
    }

    [TestCase(false), TestCase(true)]
    public void UnfilteredHandOperationsPreservePublicProjectionAndPhysicalRows(bool cost) {
        List<Card> deck = OrderedDeck();
        deck[2] = new([], 1, id: "d", drawCount: 1, preDrawRemovalCount: cost ? 1 : 0, postDrawDiscardCount: cost ? 0 : 1);
        CheckSearch(deck, 2);
    }

    [Test]
    public void InertEffectsEmptyEligibilityAndZeroCapacityRetainPublicRows() {
        List<Card> deck = OrderedDeck();
        deck[2] = deck[2].WithPreDrawDeckRemovalCount(60);
        deck[3] = deck[3].WithPreDrawDeckRemovalCount(60);
        CheckSearch(deck, 2);
        CheckSearch(deck, 5);
        deck[0] = deck[0].WithSearch(new(CardSelector.For(Material), false));
        deck[2] = deck[2].WithPreDrawDeckRemovalCount(1);
        CheckSearch(deck, 2);
    }

    [TestCase(false), TestCase(true)]
    public void GroupedLevelCostUsesOriginalObjectiveIdentity(bool manuallyAssignedLevel) {
        CategoryBase monster = new("Monster", CategorySource.Metadata, "kind:monster");
        CategoryBase level = new("Level 8", CategorySource.Metadata, "level:8");
        CardSelector filter = CardSelector.AllOfSelectors([CardSelector.LevelAtLeast(5),
            CardSelector.AnyOfSelectors([CardSelector.For(A), CardSelector.For(Material)])]);
        List<Card> deck = OrderedDeck();
        deck[1] = new([A, monster, level], 1, "same", id: "a", externalCardId: 1,
            manualMetadataCategoryKeys: manuallyAssignedLevel ? ["level:8"] : []);
        deck[2] = new([], 1, "same", id: "d", drawCount: 1, preDrawRemovalCount: 1, preDrawSelector: filter);
        deck[4] = new([Material, monster, level], 1, id: "f", externalCardId: 2);
        CheckSearch(deck, 2);
    }

    [Test]
    public void UnpayableAndDeferredHandCostsPreservePublicRows() {
        List<Card> deck = [new([], 1, id: "s", search: new(CardSelector.For(A), false)),
            new([A], 1, id: "d", drawCount: 1, preDrawRemovalCount: 2)];
        CheckSearch(deck, 1);
        deck.Add(new([], 1, id: "e", drawCount: 1));
        deck.Add(new([], 1, id: "f"));
        CheckSearch(deck, 1);
        // This deliberately admits a false negative: even a reachable loss need
        // not change this request's success predicates. Unproven is not inexact.
    }

    [Test]
    public void RawInactiveCardsUseExecutedInputsAndOpeningSnapshotClearsEffects() {
        List<Card> cards = [new([A], 1, active: false, id: "a"), new([], 1, id: "f")];
        ProbabilityCalculatorService service = new();
        ProbabilityCalculationResult off = service.CalculateProbabilityResults(cards, Combos, 1, Groups);
        ProbabilityCalculationResult on = service.CalculateProbabilityResults(cards, Combos, 1, Groups,
            CalculationWorkPolicy.Default, new(CertifySearchAvailability: true));
        SearchAvailabilityCertificateTest.AssertPrimary(off, on);
        Assert.That(on.ComboProbabilities[0].Probability, Is.EqualTo(0.5));
        Assert.That(on.SearchAvailability!.Status, Is.EqualTo(SearchAvailabilityStatus.NotApplicable));
        CalculationSnapshot snapshot = CalculationSnapshot.Capture(OrderedDeck(), Combos, 2, Groups,
            drawEffectsEnabled: false, options: new(CertifySearchAvailability: true));
        ProbabilityCalculationResult opening = CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json));
        Assert.That(opening.SearchAvailability, Is.Null);
        Assert.That(opening.Contract!.Reference, Is.EqualTo(CalculationResultContract.OpeningReference));
    }

    [Test]
    public void UnknownExecutionModelsFailAtTheWorkerBoundary() {
        List<Card> deck = OrderedDeck();
        deck[2] = deck[2].WithEffect(deck[2].EffectPlan, new(ExecutionModel: (EffectExecutionModel)999));
        CalculationSnapshot snapshot = CalculationSnapshot.Capture(deck, Combos, 2, Groups, options: new(CertifySearchAvailability: true));
        CalculationResponse response = JsonSerializer.Deserialize(CalculationWire.Execute(snapshot.Json), CalculationJsonContext.Default.CalculationResponse)!;
        Assert.That(response.Result, Is.Null);
        Assert.That(response.FailureKind, Is.EqualTo(CalculationFailureKind.Input));
    }

    private static ApplicationResultFractions CheckSearch(List<Card> deck, int hand, string[]? priority = null) {
        DrawActivationPriority? activation = priority is null ? null : new(DrawActivationMode.CustomPriority, priority);
        SearchCertificateOracle.Counts oracle = SearchCertificateOracle.Enumerate(deck, hand, Combos, Groups, priority);
        ProbabilityCalculatorService service = new();
        ProbabilityCalculationResult projected = service.CalculateProbabilityResults(deck, Combos, hand, Groups,
            CalculationWorkPolicy.Default, new(activation, true));
        ApplicationResultFractions result = ApplicationResultFractions.From(projected);
        AssertRows(result, oracle.Wins[2], oracle.Outcomes);
        ProbabilityCalculationResult fast = service.CalculateProbabilityResults(deck, Combos, hand, Groups,
            CalculationWorkPolicy.Default, new(activation));
        AssertRows(ApplicationResultFractions.From(fast), oracle.Wins[fast.SearchAvailability is null ? 1 : 2], oracle.Outcomes);
        ProbabilityCalculationResult exact = service.CalculateProbabilityResults(deck, Combos, hand, Groups,
            CalculationWorkPolicy.Default, new(activation, Mode: CalculationModel.Exact));
        AssertRows(ApplicationResultFractions.From(exact), oracle.Wins[1], oracle.Outcomes);
        return result;
    }

    private static void AssertRows(ApplicationResultFractions result, BigInteger[] wins, BigInteger outcomes) {
        ApplicationFraction[] rows = [result.Total, .. result.Combos, .. result.Groups];
        for (int row = 0; row < rows.Length; row++) {
            Assert.That(rows[row].Numerator * outcomes, Is.EqualTo(wins[row] * rows[row].Denominator), $"row {row}");
        }
    }
}
