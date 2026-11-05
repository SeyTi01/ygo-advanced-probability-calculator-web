using Microsoft.JSInterop;
using Moq;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.Session;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

public class DrawEffectFeasibilityTest {
    private static readonly CategoryBase A = new("A");
    private static readonly CategoryBase B = new("Monster", CategorySource.Metadata, "kind:monster");
    private static readonly CategoryBase Missing = new("Missing");
    private static readonly CardSelector Material = CardSelector.AllOf([A.Identity, B.Identity]);
    private static Card Pot(int copies = 1) => CardEffectRegistry.Default.RestoreBuiltIn(new([A], copies, id: "pot", externalCardId: 49238328));
    private static readonly List<Combo> Combos = [
        new([new(A, 1, 3)], groupId: "g"),
        new([], cards: [new("cost", 1, 2)], groupId: "g"),
        new([new(B, 0, 1)], cards: [new("target", 1, 1)]),
        new([new(A, 0, 0)], cards: [new("f", 1, 3)]),
        new([ComboCategory.SameCard([A, B], 1, 2)], cards: [new("pot", 1, 2)])
    ];
    private static readonly List<ComboGroup> Groups = [new("g", "Group"), new("empty", "Empty")];

    [TestCase(1, 0, 0), TestCase(2, 0, 0), TestCase(3, 0, 0)]
    [TestCase(2, 1, 0), TestCase(2, 0, 1), TestCase(2, 1, 1)]
    public void PhysicalCapacityIncludesTheWholeBatchAndHiddenPositions(int draw, int banish, int mill) {
        Card effect = new([A], 2, id: "cost", drawCount: draw, preDrawDeckRemovalCount: banish, postDrawDeckMillCount: mill);
        List<Card> deck = [effect, new([A, B], id: "target"), new([], 3, id: "f")];
        int consumption = draw + banish + mill;
        foreach (bool configured in new[] { true, false }) {
            int required = configured ? consumption : draw;
            List<Card> modeled = configured ? deck : deck.Select(card => card.WithPreDrawDeckRemovalCount(0).WithPostDrawDeckMillCount(0)).ToList();
            foreach (int remaining in new[] { required - 1, required, required + 1 }.Distinct()) {
                int hand = 6 - remaining;
                AssertParity(modeled, hand);
            }
        }
    }

    [TestCase(1, 1, 0), TestCase(1, 1, 1), TestCase(2, 1, 0)]
    [TestCase(1, 2, 1), TestCase(2, 2, 0), TestCase(2, 2, 1), TestCase(3, 2, 0)]
    public void CostBoundReservesOneCopyAndCountsAllOtherSourceCopies(int copies, int cost, int extra) {
        Card effect = new([A, B], copies, id: "cost", drawCount: 1,
            preDrawRemovalCount: cost, preDrawSelector: Material);
        List<Card> deck = [effect, new([A, B], extra, id: "target"), new([], 3, id: "f")];
        AssertParity(deck, 1);
        AssertParity(deck, 2);
    }

    [TestCase(false), TestCase(true)]
    public void InertSecondEffectEnablesPotProofUnderBothPolicies(bool custom) {
        Card cost = new([B], 2, id: "cost", drawCount: 2, preDrawRemovalCount: 1, preDrawSelector: Material);
        List<Card> deck = [Pot(2), cost, new([A], id: "target"), new([], 2, id: "f")];
        DrawActivationPriority priority = custom ? new(DrawActivationMode.CustomPriority, ["cost", "pot"]) : DrawActivationPriority.Default;
        AssertParity(deck, 2, priority: priority);
        AssertParity(deck, 2, false, priority);
        // Both retained Pot copies and drawn cost cards remain direct/category targets.
        Assert.That(deck[1], Is.SameAs(cost));
        Assert.That(cost.PreDrawRemovalCount, Is.EqualTo(1));
    }

    [Test]
    public void CapacityInertSecondEffectEnablesPotWithoutWeakeningCollectiveBound() {
        List<Card> deck = [Pot(2), new([B], id: "cost", drawCount: 3), new([A], id: "target"), new([], 2, id: "f")];
        AssertParity(deck, 4);
        AssertParity(deck, 3);
        AssertParity([.. deck, Pot().WithName("Independent Pot").Duplicate()], 4);
    }

    [Test]
    public void AnotherDrawCanSupplyCostAndIndependentOptAllowancesMustSurvive() {
        Card cost = new([B], id: "cost", drawCount: 1, preDrawRemovalCount: 1, preDrawSelector: Material);
        Card draw = new([], id: "draw", drawCount: 1);
        Card target = new([A, B], id: "target");
        List<Card> deck = [cost, draw, target, new([], 2, id: "f")];
        Assert.That(DrawSequenceOracle.Resolve([cost, draw, target, new([], id: "f"), new([], id: "last")], 2, [])!
            .Any(card => card.Id == "cost"), Is.False, "Cost becomes payable after the other draw");
        AssertParity(deck, 2);
        AssertParity([.. deck, draw.Duplicate()], 2);
        AssertParity([cost.WithDrawOncePerTurn(false).WithCopies(2), .. deck.Skip(1)], 2);
    }

    [Test]
    public void HandSelectionKeepsOriginalEffectClassificationAndPostRemovalFailureMass() {
        Card inert = new([], id: "cost", drawCount: 1, preDrawRemovalCount: 1, preDrawSelector: CardSelector.For(Missing));
        foreach (bool preCost in new[] { false, true }) {
            Card live = new([], id: "draw", drawCount: 2,
                preDrawRemovalCount: preCost ? 1 : 0, postDrawDiscardCount: preCost ? 0 : 1);
            List<Card> deck = [inert, live, new([A, B], id: "target"), new([], 3, id: "f")];
            foreach (int hand in new[] { 2, 3, 4 }) {
                AssertParity(deck, hand);
            }
        }
        List<Card> failure = [inert, new([], id: "draw", drawCount: 1, postDrawDiscardCount: 2,
            postDrawSelector: Material), new([A, B], id: "target"), new([], 2, id: "f")];
        DrawSequenceOracle.Counts oracle = AssertParity(failure, 2);
        Assert.That(oracle.Failed, Is.GreaterThan(System.Numerics.BigInteger.Zero), "A post-draw failure must remain failed denominator mass");
    }

    [Test]
    public void NaiveOrdinaryConversionWouldChangeTheDeferredRemovalPolicy() {
        Card inert = new([B], id: "cost", drawCount: 1, preDrawRemovalCount: 1, preDrawSelector: CardSelector.For(Missing));
        Card live = new([], id: "draw", drawCount: 2, preDrawRemovalCount: 1);
        List<Card> order = [live, inert, new([], id: "f"), new([], id: "x"), new([], id: "y"), new([], id: "z")];
        List<Combo> excludeInert = [new([new(B, 0, 0)])];
        List<Card> originalHand = DrawSequenceOracle.Resolve(order, 3, excludeInert)!;
        List<Card> naiveHand = DrawSequenceOracle.Resolve(order.Select(card => card == inert ? card.WithDrawCount(null) : card).ToList(), 3, excludeInert)!;
        Assert.That(originalHand.Any(card => card.Id == inert.Id), Is.True);
        Assert.That(naiveHand.Any(card => card.Id == inert.Id), Is.False);
        DrawSequenceOracle.Counts oracle = DrawSequenceOracle.Enumerate(order, 3, excludeInert, [], true);
        ApplicationFraction actual = ApplicationResultFractions.From(new ProbabilityCalculatorService().CalculateProbabilityResults(
            order, excludeInert, 3, [], CalculationWorkPolicy.Interactive, new(Mode: CalculationModel.Exact))).Total;
        Assert.That(actual.Numerator * oracle.Total, Is.EqualTo(oracle.Wins[0] * actual.Denominator));
    }

    [Test]
    public void CompositeSelectorsUseEffectiveMembershipAndIgnoreInactiveAndZeroCopies() {
        CardSelector selector = CardSelector.AnyOfSelectors([Material, CardSelector.For(Missing)]);
        Card cost = new([], id: "cost", drawCount: 1, preDrawRemovalCount: 2, preDrawSelector: selector);
        List<Card> cards = [cost, new([A], id: "partial"), new([A, B], id: "target"), new([Missing], 0, id: "zero"),
            new([Missing], 2, active: false, id: "inactive"), new([], 3, id: "f")];
        AssertParity(cards.Where(card => card.Active).ToList(), 2);
        CalculationSnapshot snapshot = CalculationSnapshot.Capture(cards, Combos, 2, Groups, options: new());
        Assert.That(CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json)).TotalProbability,
            Is.EqualTo(new ProbabilityCalculatorService().CalculateProbabilityResults(cards.Where(card => card.Active).ToList(), Combos, 2, Groups,
                CalculationWorkPolicy.Interactive, new CalculationOptions()).TotalProbability));
    }

    [Test]
    public async Task SessionsAndWorkerKeepPlansAndOpeningHandModeIgnoresAllDraws() {
        Card cost = new([B], id: "cost", drawCount: 2, preDrawRemovalCount: 1, preDrawSelector: Material);
        SessionService codec = new(Mock.Of<IJSRuntime>(), new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());
        foreach (Card pot in new[] { Pot(2), new([A], 2, id: "pot", externalCardId: 49238328, drawCount: 2,
            effectProfile: new(EffectSource.BuiltIn, 49238328, 1, "Draw 2; once per turn.", EffectSupport.Partial)) }) {
            SessionState original = new() { Cards = [pot, cost, new([A], id: "target"), new([], 3, id: "f")],
                Combos = Combos, ComboGroups = Groups, HandSize = 2 };
            string saved = codec.SerializeSession(original);
            SessionState loaded = await codec.LoadSessionAsync(saved);
            AssertParity(loaded.Cards, 2);
            AssertParity(loaded.Cards, 2, false);
            Assert.That(codec.SerializeSession(original), Is.EqualTo(saved));
            Assert.That(loaded.Cards[1].EffectPlan, Is.EqualTo(cost.EffectPlan));
            ApplicationResultFractions opening = ApplicationResultFractions.From(new ProbabilityCalculatorService().CalculateProbabilityResults(
                loaded.Cards, Combos, 2, Groups, CalculationWorkPolicy.Interactive, new(Mode: CalculationModel.OpeningHand)));
            ProbabilityCalculationResult worker = CalculationWire.ReadResult(CalculationWire.Execute(CalculationSnapshot.Capture(
                loaded.Cards, Combos, 2, Groups, drawEffectsEnabled: false).Json));
            Assert.That(worker.TotalProbability, Is.EqualTo(opening.Public!.TotalProbability));
        }
    }

    [Test]
    public async Task ExamplePotWithMissingCostMaterialFitsStandardStorage() {
        SessionService codec = new(Mock.Of<IJSRuntime>(), new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());
        SessionState session = await codec.LoadSessionAsync(File.ReadAllText(Path.Combine(
            TestContext.CurrentContext.TestDirectory, "Fixtures", "example_session_state.json")));
        Card original = session.Cards.Single(card => card.Name == "Mulcharmy Fuwalos");
        Card pot = CardEffectRegistry.Default.RestoreBuiltIn(new([], original.Copies, id: original.Id, externalCardId: 49238328));
        session.Cards[session.Cards.IndexOf(original)] = pot;
        Card ordinary = session.Cards.First(card => card != pot && card.Copies > 0);
        Card inert = ordinary.WithDrawCount(2).WithPreDrawRemovalCount(1).WithPreDrawSelector(CardSelector.For(Missing));
        session.Cards[session.Cards.IndexOf(ordinary)] = inert;
        List<Card> deck = CategoryMembership.Materialize(session.Cards, session.Categories).Where(card => card.Active).ToList();
        List<Combo> combos = session.Combos.Where(combo => combo.Active).ToList();
        Assert.That(deck.Sum(card => card.Copies), Is.EqualTo(40));
        foreach (DrawActivationPriority priority in new[] { DrawActivationPriority.Default,
            new DrawActivationPriority(DrawActivationMode.CustomPriority, [inert.Id, pot.Id]) }) {
            ProbabilityCalculatorService service = new();
            ApplicationResultFractions actual = ApplicationResultFractions.From(service.CalculateProbabilityResults(
                deck, combos, 5, session.ComboGroups, CalculationWorkPolicy.Interactive, CalculationStoragePolicy.Standard, new(priority, Mode: CalculationModel.Exact)));
            ApplicationResultFractions expected = ApplicationResultFractions.From(service.CalculateProbabilityResults(
                deck.Select(card => card.Id == inert.Id ? ordinary : card).ToList(), combos, 5, session.ComboGroups,
                CalculationWorkPolicy.Interactive, new(priority, Mode: CalculationModel.Exact)));
            ApplicationFraction[] rows = [actual.Total, .. actual.Combos, .. actual.Groups];
            ApplicationFraction[] reference = [expected.Total, .. expected.Combos, .. expected.Groups];
            for (int i = 0; i < rows.Length; i++) {
                Assert.That(rows[i].Numerator * reference[i].Denominator, Is.EqualTo(reference[i].Numerator * rows[i].Denominator));
            }
            ProbabilityCalculationResult worker = CalculationWire.ReadResult(CalculationWire.Execute(CalculationSnapshot.Capture(
                session.Cards, combos, 5, session.ComboGroups, options: new(priority), categoryDefinitions: session.Categories).Json));
            Assert.That(worker.TotalProbability, Is.EqualTo(actual.Public!.TotalProbability));
        }
        Assert.That(inert.DrawCount, Is.EqualTo(2));
    }

    [Test]
    public void InvalidExecutableSnapshotStillFailsWhenCapacityOrMaterialIsMissing() {
        Card invalid = new([], id: "cost", drawCount: 3, preDrawRemovalCount: 1, preDrawSelector: Material,
            effectProfile: new(ExecutionModel: EffectExecutionModel.Unsupported));
        List<Card> deck = [invalid, new([], id: "f")];
        Assert.Throws<ArgumentException>(() => new ProbabilityCalculatorService().CalculateProbabilityResults(deck, Combos, 1));
    }

    private static DrawSequenceOracle.Counts AssertParity(List<Card> deck, int hand, bool configured = true, DrawActivationPriority? priority = null) {
        priority ??= DrawActivationPriority.Default;
        List<Card> modeled = configured ? deck : deck.Select(card => card.WithPreDrawRemovalCount(0).WithPostDrawDiscardCount(0)
            .WithPreDrawDeckRemovalCount(0).WithPostDrawDeckMillCount(0)).ToList();
        DrawSequenceOracle.Counts oracle = DrawSequenceOracle.Enumerate(modeled, hand, Combos, Groups, true,
            priority.Mode == DrawActivationMode.CustomPriority ? priority.CardIds : null);
        ProbabilityCalculatorService service = new();
        ApplicationResultFractions result = ApplicationResultFractions.From(service.CalculateProbabilityResults(
            modeled, Combos, hand, Groups, CalculationWorkPolicy.Interactive, new(priority, Mode: CalculationModel.Exact)));
        ApplicationResultFractions ordered = ApplicationResultFractions.From(service.CalculateProbabilityResults(
            modeled, Combos, hand, Groups, CalculationWorkPolicy.Interactive, new(priority)));
        ApplicationFraction[] rows = [result.Total, .. result.Combos, .. result.Groups];
        ApplicationFraction[] reference = [ordered.Total, .. ordered.Combos, .. ordered.Groups];
        for (int i = 0; i < rows.Length; i++) {
            Assert.That(rows[i].Numerator * reference[i].Denominator, Is.EqualTo(reference[i].Numerator * rows[i].Denominator));
            Assert.That(rows[i].Numerator * oracle.Total, Is.EqualTo(oracle.Wins[i] * rows[i].Denominator), $"row={i}, hand={hand}, configured={configured}");
        }
        ProbabilityCalculationResult worker = CalculationWire.ReadResult(CalculationWire.Execute(CalculationSnapshot.Capture(
            modeled, Combos, hand, Groups, options: new(priority)).Json));
        Assert.That(worker.TotalProbability, Is.EqualTo(result.Public!.TotalProbability));
        Assert.That(worker.ComboProbabilities.Select(row => row.Probability), Is.EqualTo(result.Public!.ComboProbabilities.Select(row => row.Probability)));
        Assert.That(worker.GroupProbabilities.Select(row => row.Probability), Is.EqualTo(result.Public!.GroupProbabilities!.Select(row => row.Probability)));
        return oracle;
    }
}
