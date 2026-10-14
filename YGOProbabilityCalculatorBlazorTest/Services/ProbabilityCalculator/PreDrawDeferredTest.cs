using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public class PreDrawDeferredTest {
    private static readonly CategoryBase Material = new("Material");
    private static readonly CategoryBase Alternative = new("Alternative material");
    private static readonly CategoryBase Monster = new("Monster");
    private static readonly CategoryBase A = new("A");
    private static readonly CategoryBase B = new("B");

    private static readonly CardSelector Cost = CardSelector.AnyOfSelectors([
        CardSelector.AllOf([Material.Identity, Monster.Identity]),
        CardSelector.AllOf([Alternative.Identity, Monster.Identity])
    ]);

    private static Card Source(int copies = 1, bool opt = false, bool eligible = false, int mill = 0) =>
        new(eligible ? [Material, Monster] : [], copies, id: "source", drawCount: 2, drawOncePerTurn: opt,
            preDrawRemovalCount: 1, preDrawSelector: Cost, postDrawDeckMillCount: mill);

    private static List<Card> Deck(int copies = 1, bool opt = false, bool eligible = false, int mill = 0) => [
        Source(copies, opt, eligible, mill),
        new([Material, Monster, A, B], id: "material-ab"),
        new([Alternative, Monster, B], id: "material-b"),
        new([Material, A], id: "spell"),
        new([A, B], id: "pool-ab"),
        new([B], id: "pool-b")
    ];

    private static List<Combo> Combos() => [
        new([new(A, 1, 0, RequirementMaximumMode.HandSize), new(B, 1, 0, RequirementMaximumMode.HandSize)], groupId: "g"),
        new([ComboCategory.SameCard([A, B], 1, 1)], groupId: "g"),
        new([], cards: [new("material-ab", 1, 1), new("pool-b", 1, 1)], groupId: "other"),
        new([], cards: [new("source", 1, 2)], groupId: "other"),
        new([new(Material, 0, 0)]),
        new([], cards: [new("pool-ab", 0, 0)]),
        new([new(B, 0, 1)], cards: [new("spell", 1, 1)]),
        new([], alternativeGroups: [new([ComboAlternative.For(new ComboCategory(A, 1, 1)),
            ComboAlternative.For(new ComboCard("pool-b", 1, 1))])])
    ];

    private static List<ComboGroup> Groups() => [new("g", "Overlap"), new("other", "Direct"), new("empty", "Empty")];

    private static IEnumerable<TestCaseData> Cases() {
        foreach (int copies in new[] { 1, 2, 3 }) {
            foreach (bool opt in new[] { false, true }) {
                foreach (bool sourceEligible in new[] { false, true }) {
                    yield return new(copies, opt, sourceEligible);
                }
            }
        }
    }

    [TestCaseSource(nameof(Cases))]
    public void FilteredCostAndHiddenMillMatchEveryLabeledResult(int copies, bool opt, bool sourceEligible) {
        AssertExact(Deck(copies, opt, sourceEligible, mill: copies % 3), 2);
    }

    [TestCase(0), TestCase(1), TestCase(4)]
    public void EmptyAndSourceOnlyOpenersAndLargeHandsPreserveDenominator(int hand) {
        AssertExact(Deck(2, mill: 1), hand);
    }

    [TestCase(0), TestCase(1), TestCase(2)]
    public void AbsentOrRepeatedMaterialAndTwoCardCostsStayExact(int materialCopies) {
        List<Card> deck = Deck(2);
        deck[0] = deck[0].WithPreDrawRemovalCount(2);
        deck[1] = deck[1].WithCopies(materialCopies);
        deck.RemoveAt(2);
        DrawSequenceOracle.Counts oracle = AssertExact(deck, 3);
        Assert.That(oracle.Failed, Is.EqualTo(BigInteger.Zero));
    }

    [TestCase(3), TestCase(4)]
    public void FullDrawAndMillCapacityIsCheckedBeforePayment(int remaining) {
        Card source = Source(mill: 2);
        Card material = new([Material, Monster], id: "material-ab");
        List<Card> deck = [source, material, new([A, B], id: "pool-ab"), new([B], id: "pool-b"),
            new([A], remaining - 2, id: "spell")];
        Card[] order = deck.SelectMany(card => Enumerable.Repeat(card, card.Copies)).ToArray();
        Assert.That(DrawSequenceOracle.Resolve(order, 2, Combos())!.Select(card => card.Id), remaining == 3
            ? Is.EquivalentTo(new[] { "source", "material-ab" }) : Is.EquivalentTo(new[] { "pool-ab", "pool-b" }));
        AssertExact(deck, 2);
    }

    [TestCase(false), TestCase(true)]
    public void NewlyDrawnSourceAndMaterialCanActivateAfterUnseenPositions(bool enoughForSecondBatch) {
        Card source = Source(2, mill: 2);
        Card material = new([Material, Monster], 2, id: "material-ab");
        Card target = new([A, B], id: "pool-ab");
        Card filler = new([B], enoughForSecondBatch ? 5 : 4, id: "pool-b");
        Card[] order = [source, material, source, material, target, .. Enumerable.Repeat(filler, filler.Copies)];
        List<Card> hand = DrawSequenceOracle.Resolve(order, 2, Combos())!;
        Assert.That(hand.Select(card => card.Id), enoughForSecondBatch
            ? Is.EquivalentTo(new[] { "pool-b", "pool-b" }) : Is.EquivalentTo(new[] { "source", "material-ab" }));
        // The named target is physically milled in this ordering, although it remains
        // part of the solver's unobserved identity pool rather than an observed draw.
        AssertExact([source, material, target, filler], 2);
    }

    [TestCase(0), TestCase(1)]
    public void JointFilteredPreAndPostRemovalRetainsUnionOfEligibleMaterial(int hiddenCost) {
        List<Card> deck = Deck(2, eligible: true, mill: 1);
        deck[0] = deck[0].WithPostDrawDiscardCount(1).WithPostDrawSelector(CardSelector.For(Alternative))
            .WithPreDrawDeckRemovalCount(hiddenCost);
        DrawSequenceOracle.Counts oracle = AssertExact(deck, 2);
        Assert.That(oracle.Failed, Is.GreaterThan(BigInteger.Zero));
        Assert.That(oracle.Wins[0], Is.GreaterThan(BigInteger.Zero));
        Assert.That(oracle.Wins[0], Is.LessThan(oracle.Total));
    }

    [Test]
    public void SourceAndOrdinaryMaterialTieUsesFullComboRelevance() {
        List<Card> deck = Deck(3, eligible: true);
        deck.Insert(1, new([Material, Monster], id: "irrelevant-material"));
        // Maximum-only source reference gives it no positive protection. A source
        // and ordinary material can have the same relevant-category membership.
        List<Combo> combos = [new([], cards: [new("source", 0, 1)]),
            new([], cards: [new("material-ab", 1, 1)], groupId: "g"),
            new([], cards: [new("pool-ab", 1, 1)]), new([], cards: [new("pool-b", 0, 0)])];
        AssertExact(deck, 3, combos);
    }

    [Test]
    public void UnsafeSelectionAndTimingShapesKeepOrderedFallback() {
        Card source = Source(2, mill: 1);
        foreach (Card unsafeSource in new[] {
            source.WithPreDrawSelector(CardSelector.Any),
            source.WithPostDrawDiscardCount(1),
            source.WithEffect(source.EffectPlan with { FirstActivationOnly = true }, source.EffectProfile),
            source.WithEffect(source.EffectPlan with { BlocksLaterDraws = true }, source.EffectProfile)
        }) {
            List<Card> deck = Deck(2, mill: 1);
            deck[0] = unsafeSource;
            AssertExact(deck, 2, deferred: false);
        }
        List<Card> mixed = Deck(1);
        mixed.Add(new([], id: "other", drawCount: 1));
        AssertExact(mixed, 2);
    }

    [Test]
    public void WireModesIgnoreInactiveAndZeroCopyEntriesWithoutChangingSavedEffects() {
        List<Card> active = Deck(2, mill: 1);
        List<Card> deck = [.. active, new([Material, Monster], 3, active: false, id: "inactive", drawCount: 3),
            new([], 0, id: "zero", drawCount: 1, preDrawRemovalCount: 1)];
        List<Combo> combos = Combos();
        List<ComboGroup> groups = Groups();
        DrawEffectPlan saved = deck[0].EffectPlan;
        foreach ((bool enabled, bool configured) in new[] { (true, true), (true, false), (false, true) }) {
            List<Card> modeled = active.Select(card => !enabled ? card.WithDrawCount(null)
                : !configured && card.DrawCount is not null
                    ? card.WithPreDrawRemovalCount(0).WithPreDrawDeckRemovalCount(0).WithPostDrawDiscardCount(0).WithPostDrawDeckMillCount(0)
                    : card).ToList();
            DrawSequenceOracle.Counts expected = DrawSequenceOracle.Enumerate(modeled, 2, combos, groups, collapseIdenticalCopies: true);
            CalculationSnapshot snapshot = CalculationSnapshot.Capture(deck, combos, 2, groups,
                CalculationWorkPolicy.Interactive, enabled, options: new(configured));
            ProbabilityCalculationResult actual = CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json));
            double[] rows = [actual.TotalProbability, .. actual.ComboProbabilities.Select(row => row.Probability),
                .. actual.GroupProbabilities.Select(row => row.Probability)];
            Assert.That(rows, Is.EqualTo(expected.Wins.Select(win => (double)win / (double)expected.Total)).Within(1e-14));
        }
        Assert.That(deck[0].EffectPlan, Is.EqualTo(saved));
    }

    [TestCase(1), TestCase(3)]
    public async Task CompleteSolarExampleUsesDeferredRouteWithinStandardBudget(int copies) {
        SessionState session = await RealisticSolarFixture.Load(copies);
        WorkBudget budget = new(CalculationWorkPolicy.Interactive);
        PostDrawCalculation calculation = new(session.Cards, session.HandSize, budget);
        ExactCalculationResult result = calculation.Calculate(session.Combos, session.ComboGroups);
        Assert.Multiple(() => {
            Assert.That(session.Cards.Sum(card => card.Copies), Is.EqualTo(40));
            Assert.That(session.Combos, Has.Count.EqualTo(8));
            Assert.That(session.ComboGroups, Has.Count.EqualTo(2));
            Assert.That(result.Combos, Has.Length.EqualTo(8));
            Assert.That(result.Groups, Has.Length.EqualTo(2));
            Assert.That(result.Total.Numerator, Is.GreaterThan(BigInteger.Zero));
            Assert.That(result.Total.Numerator, Is.LessThan(result.Total.Denominator));
            Assert.That(calculation.DeferredRows, Is.GreaterThan(1));
            Assert.That(DrawExecutionAssessment.Assess(session.Cards, session.Combos, session.HandSize, true, true).Path,
                Is.EqualTo(DrawExecutionPath.DeferredConditional));
        });
    }

    private static DrawSequenceOracle.Counts AssertExact(List<Card> deck, int hand, List<Combo>? combos = null, bool deferred = true) {
        combos ??= Combos();
        List<ComboGroup> groups = Groups();
        DrawSequenceOracle.Counts oracle = DrawSequenceOracle.Enumerate(deck, hand, combos, groups, collapseIdenticalCopies: true);
        foreach (bool reversed in new[] { false, true }) {
            List<Card> ordered = reversed ? deck.AsEnumerable().Reverse().ToList() : deck;
            DrawActivationPriority priority = reversed
                ? new(DrawActivationMode.CustomPriority, ["other", "source"]) : DrawActivationPriority.Default;
            DrawSequenceOracle.Counts expected = reversed
                ? DrawSequenceOracle.Enumerate(deck, hand, combos, groups, collapseIdenticalCopies: true, priorityIds: priority.CardIds) : oracle;
            PostDrawCalculation calculation = new(ordered, hand, new(CalculationWorkPolicy.Interactive), priority);
            AssertRows(calculation.Calculate(combos, groups), expected);
            Assert.That(calculation.DeferredRows > 0, Is.EqualTo(deferred));
            AssertRows(new PostDrawCalculation(ordered, hand, new(CalculationWorkPolicy.Interactive), priority,
                deferIdentities: false).Calculate(combos, groups), expected);
            ExactCalculationResult total = new PostDrawCalculation(ordered, hand, new(CalculationWorkPolicy.Interactive), priority)
                .Calculate(combos, groups, totalOnly: true);
            Assert.That(total.Total.Numerator * expected.Total, Is.EqualTo(expected.Wins[0] * total.Total.Denominator));
        }
        AssertRows(new DrawEffectCalculation(deck, hand, new(CalculationWorkPolicy.Interactive), true).Calculate(combos, groups), oracle);
        return oracle;
    }

    private static void AssertRows(ExactCalculationResult result, DrawSequenceOracle.Counts oracle) {
        ExactProbability[] rows = [result.Total, .. result.Combos, .. result.Groups];
        Assert.That(rows, Has.Length.EqualTo(oracle.Wins.Length));
        for (int i = 0; i < rows.Length; i++) {
            Assert.That(rows[i].Numerator * oracle.Total, Is.EqualTo(oracle.Wins[i] * rows[i].Denominator), $"exact row {i}");
        }
    }
}
