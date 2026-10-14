using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public class MixedDeferredTest {
    private static readonly CategoryBase Cost = new("Cost");
    private static readonly CategoryBase Removal = new("Removal");
    private static readonly CategoryBase A = new("A");
    private static readonly CategoryBase B = new("B");

    private static List<Card> Deck() => [
        new([Removal], name: "Same name", id: "cost", externalCardId: 1475311, drawCount: 2,
            preDrawRemovalCount: 1, preDrawSelector: CardSelector.For(Cost)),
        new([Cost], name: "Same name", id: "remove", externalCardId: 1475311, drawCount: 2,
            drawOncePerTurn: false, postDrawDiscardCount: 1, postDrawSelector: CardSelector.For(Removal)),
        new([Cost, Removal, A, B], id: "both"), new([Removal, B], id: "removal-only"),
        new([A, B], id: "pool-ab"), new([B], id: "pool-b"), new([], id: "pool-blank")
    ];

    private static List<Combo> Combos() => [
        new([new(A, 1, 0, RequirementMaximumMode.HandSize), new(B, 1, 0, RequirementMaximumMode.HandSize)], groupId: "g"),
        new([ComboCategory.SameCard([A, B], 1, 1)], groupId: "g"),
        new([], cards: [new("pool-b", 1, 1), new("both", 1, 1)], groupId: "other"),
        new([], cards: [new("cost", 1, 2)], groupId: "other"),
        new([new(Cost, 0, 0)]), new([], cards: [new("pool-ab", 0, 0)]),
        new([], cards: [new("pool-blank", 0, 0)]),
        new([new(B, 0, 1)], cards: [new("remove", 1, 1)]),
        new([], alternativeGroups: [new([ComboAlternative.For(new ComboCategory(A, 1, 1)),
            ComboAlternative.For(new ComboCard("pool-b", 1, 1))])])
    ];

    private static List<ComboGroup> Groups() => [new("g", "Overlap"), new("other", "Direct"), new("empty", "Empty")];

    [TestCase("pre-post"), TestCase("post-post"), TestCase("composite"), TestCase("two-costs")]
    [TestCase("hidden-pre"), TestCase("hidden-post"), TestCase("hidden-both"), TestCase("three-sources")]
    [TestCase("copies-opt"), TestCase("copies-non-opt"), TestCase("failure")]
    public void JointTraceMatchesLabeledPermutationOracle(string shape) {
        List<Card> deck = Deck();
        switch (shape) {
            case "post-post":
                deck[0] = deck[0].WithPreDrawRemovalCount(0).WithPostDrawDiscardCount(1).WithPostDrawSelector(CardSelector.For(Cost));
                break;
            case "composite":
                deck[0] = deck[0].WithPreDrawSelector(CardSelector.AnyOfSelectors([
                    CardSelector.AllOf([Cost.Identity, A.Identity]), CardSelector.AllOf([Cost.Identity, B.Identity])
                ]));
                deck[1] = deck[1].WithPostDrawSelector(CardSelector.AllOf([Removal.Identity, B.Identity]));
                break;
            case "two-costs":
                deck[0] = deck[0].WithPreDrawRemovalCount(2);
                break;
            case "hidden-pre":
                deck[1] = deck[1].WithPreDrawDeckRemovalCount(1);
                break;
            case "hidden-post":
                deck[0] = deck[0].WithPostDrawDeckMillCount(1);
                break;
            case "hidden-both":
                deck[0] = deck[0].WithPostDrawDeckMillCount(1);
                deck[1] = deck[1].WithPreDrawDeckRemovalCount(1);
                break;
            case "three-sources":
                deck.Add(new([], id: "draw", drawCount: 1));
                break;
            case "copies-opt":
                deck[0] = deck[0].WithCopies(2);
                deck[1] = deck[1].WithDrawOncePerTurn(true);
                break;
            case "copies-non-opt":
                deck[0] = deck[0].WithCopies(2).WithDrawOncePerTurn(false);
                break;
            case "failure":
                deck[1] = deck[1].WithPostDrawDiscardCount(2);
                break;
        }
        DrawSequenceOracle.Counts expected = AssertExact(deck, 2);
        Assert.That(expected.Failed, Is.GreaterThan(BigInteger.Zero));
        Assert.That(expected.Wins[0], Is.InRange(BigInteger.One, expected.Total - 1));
    }

    [TestCase(1), TestCase(3), TestCase(5)]
    public void AtomicBatchExhaustionAndOpeningQueueOrderStayExact(int hand) {
        AssertExact(Deck(), hand);
    }

    [Test]
    public void LaterDrawPaysDeferredCostAndAnotherEffectCanConsumeAQueuedSource() {
        Card cost = new([], id: "cost", drawCount: 1, preDrawRemovalCount: 1, preDrawSelector: CardSelector.For(Cost));
        Card draw = new([], id: "draw", drawCount: 1);
        Card material = new([Cost], id: "material");
        Card target = new([A], id: "pool-ab");
        Card blank = new([B], id: "pool-b");
        Assert.That(DrawSequenceOracle.Resolve([cost, draw, material, target, blank], 2, Combos())!.Select(card => card.Id),
            Is.EqualTo(new[] { "pool-ab" }));
        AssertExact([cost, draw, material, target, blank], 2);

        Card remover = new([], id: "remove", drawCount: 1, postDrawDiscardCount: 1, postDrawSelector: CardSelector.For(Removal));
        Card blocked = new([Removal], id: "cost", drawCount: 2, preDrawRemovalCount: 1, preDrawSelector: CardSelector.For(Cost));
        Assert.That(DrawSequenceOracle.Resolve([blocked, remover, target, blank, material], 2, Combos())!.Select(card => card.Id),
            Is.EqualTo(new[] { "pool-ab" }));
        AssertExact([blocked, remover, target, blank, material], 2);
    }

    [Test]
    public void EligibilityForEitherSourceBreaksLabelSymmetry() {
        List<Card> deck = Deck();
        Card source = deck[1];
        // These labels are both ineligible for the FIRST source's cost. Only
        // removal-only is eligible for the SECOND source's mandatory removal.
        Card eligible = deck[3];
        Card pooled = deck[4];
        Assert.That(Cost.Identity, Is.Not.EqualTo(Removal.Identity));
        Assert.That(DrawSequenceOracle.Resolve([source, eligible, deck[5], pooled], 1, Combos()), Is.Not.Null);
        Assert.That(DrawSequenceOracle.Resolve([source, pooled, deck[5], eligible], 1, Combos()), Is.Null);
        PostDrawCalculation calculation = new(deck, 2, new(CalculationWorkPolicy.Interactive));
        calculation.Calculate(Combos(), Groups());
        Assert.That(calculation.DeferredRows, Is.EqualTo(3));
        AssertExact(deck, 2);
    }

    [Test]
    public void UnfilteredAndUnprovedTimingShapesKeepFallback() {
        List<Card> original = Deck();
        Card source = original[1];
        foreach (Card unsafeSource in new[] {
            source.WithPostDrawSelector(CardSelector.Any),
            source.WithPreDrawRemovalCount(1),
            source.WithEffect(source.EffectPlan with { FirstActivationOnly = true }, source.EffectProfile),
            source.WithEffect(source.EffectPlan with { BlocksLaterDraws = true }, source.EffectProfile)
        }) {
            List<Card> deck = [.. original];
            deck[1] = unsafeSource;
            AssertExact(deck, 2, deferred: false);
        }
    }

    [Test]
    public void OneSafeOrdinaryIdentityDoesNotEnterDeferredRoute() {
        List<Card> deck = Deck();
        deck.RemoveAll(card => card.Id is "pool-b" or "pool-blank");
        deck.Add(new([], 0, id: "pool-blank"));
        AssertExact(deck, 2, deferred: false);
    }

    [Test]
    public void WireModesAndInactiveEntriesPreserveEffectivePlans() {
        List<Card> active = Deck();
        List<Card> deck = [.. active, new([], 3, active: false, id: "inactive", drawCount: 3, preDrawRemovalCount: 1),
            new([], 0, id: "zero", drawCount: 2, postDrawDiscardCount: 1)];
        DrawActivationPriority priority = new(DrawActivationMode.CustomPriority, ["remove", "cost"]);
        foreach ((bool enabled, bool configured) in new[] { (true, true), (true, false), (false, true) }) {
            List<Card> modeled = active.Select(card => !enabled ? card.WithDrawCount(null) : !configured
                ? card.WithPreDrawRemovalCount(0).WithPostDrawDiscardCount(0) : card).ToList();
            DrawSequenceOracle.Counts expected = DrawSequenceOracle.Enumerate(modeled, 2, Combos(), Groups(),
                collapseIdenticalCopies: true, priorityIds: priority.CardIds);
            CalculationSnapshot snapshot = CalculationSnapshot.Capture(deck, Combos(), 2, Groups(),
                CalculationWorkPolicy.Interactive, enabled, options: new(configured, priority));
            ProbabilityCalculationResult actual = CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json));
            Assert.That(new[] { actual.TotalProbability }.Concat(actual.ComboProbabilities.Select(row => row.Probability))
                .Concat(actual.GroupProbabilities.Select(row => row.Probability)),
                Is.EqualTo(expected.Wins.Select(win => (double)win / (double)expected.Total)).Within(1e-14));
        }
        Assert.That(DrawExecutionAssessment.Assess(deck, Combos(), 2, true, true).Path, Is.EqualTo(DrawExecutionPath.DeferredConditional));
        Assert.That(deck[0].PreDrawRemovalCount, Is.EqualTo(1));
        Assert.That(deck[1].PostDrawDiscardCount, Is.EqualTo(1));
    }

    [TestCase(true), TestCase(false)]
    public async Task CompleteMixedExampleUsesStandardStorage(bool vision) {
        SessionState session = await RealisticMixedFixture.Load(vision);
        PostDrawCalculation calculation = new(session.Cards, 5, new(CalculationWorkPolicy.Interactive));
        ExactCalculationResult result = calculation.Calculate(session.Combos, session.ComboGroups);
        Assert.That(result.Combos, Has.Length.EqualTo(8));
        Assert.That(result.Groups, Has.Length.EqualTo(2));
        Assert.That(calculation.DeferredRows, Is.GreaterThan(1));
        Assert.That(DrawExecutionAssessment.Assess(session.Cards, session.Combos, 5, true, true).Path,
            Is.EqualTo(DrawExecutionPath.DeferredConditional));
    }

    private static DrawSequenceOracle.Counts AssertExact(List<Card> deck, int hand, bool deferred = true) {
        DrawSequenceOracle.Counts encounter = null!;
        foreach (int order in new[] { 0, 1, 2 }) {
            bool custom = order != 0;
            // In the partial ranking both conditional sources tie and retain FIFO.
            DrawActivationPriority priority = custom ? new(DrawActivationMode.CustomPriority,
                order == 1 ? ["remove", "draw", "cost"] : ["draw"]) : DrawActivationPriority.Default;
            DrawSequenceOracle.Counts expected = DrawSequenceOracle.Enumerate(deck, hand, Combos(), Groups(),
                collapseIdenticalCopies: true, priorityIds: custom ? priority.CardIds : null);
            if (!custom) {
                encounter = expected;
            }
            foreach (List<Card> ordered in new[] { deck, deck.AsEnumerable().Reverse().ToList() }) {
                PostDrawCalculation calculation = new(ordered, hand, new(CalculationWorkPolicy.Interactive), priority);
                AssertRows(calculation.Calculate(Combos(), Groups()), expected);
                Assert.That(calculation.DeferredRows > 0, Is.EqualTo(deferred));
                AssertRows(new PostDrawCalculation(ordered, hand, new(CalculationWorkPolicy.Interactive), priority,
                    deferIdentities: false).Calculate(Combos(), Groups()), expected);
                // Exercise the representation change even on these small labeled decks,
                // with and without deferred identities. Keep the oracle independent.
                foreach (bool pool in new[] { false, true }) {
                    PostDrawCalculation packed = new(ordered, hand, new(CalculationWorkPolicy.Interactive), priority,
                        deferIdentities: pool, compactFrontierThreshold: 0);
                    AssertRows(packed.Calculate(Combos(), Groups()), expected);
                    Assert.That(packed.PackedLayers, Is.GreaterThan(0));
                }
                AssertRows(new DrawEffectCalculation(ordered, hand, new(CalculationWorkPolicy.Interactive), true, priority)
                    .Calculate(Combos(), Groups()), expected);
                ExactCalculationResult total = new PostDrawCalculation(ordered, hand, new(CalculationWorkPolicy.Interactive), priority)
                    .Calculate(Combos(), Groups(), totalOnly: true);
                Assert.That(total.Total.Numerator * expected.Total, Is.EqualTo(expected.Wins[0] * total.Total.Denominator));
            }
        }
        Assert.That(DrawExecutionAssessment.Assess(deck, Combos(), hand, true, true).Path,
            Is.EqualTo(deferred ? DrawExecutionPath.DeferredConditional : DrawExecutionPath.OrderedFrontier));
        return encounter;
    }

    private static void AssertRows(ExactCalculationResult result, DrawSequenceOracle.Counts expected) {
        ExactProbability[] rows = [result.Total, .. result.Combos, .. result.Groups];
        Assert.That(rows, Has.Length.EqualTo(expected.Wins.Length));
        for (int i = 0; i < rows.Length; i++) {
            Assert.That(rows[i].Numerator * expected.Total, Is.EqualTo(expected.Wins[i] * rows[i].Denominator), $"row {i}");
        }
    }
}
