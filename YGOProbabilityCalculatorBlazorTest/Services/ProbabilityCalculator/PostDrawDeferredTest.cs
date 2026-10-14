using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public class PostDrawDeferredTest {
    private static readonly CategoryBase Dark = new("DARK");
    private static readonly CategoryBase A = new("A");
    private static readonly CategoryBase B = new("B");

    private static List<Card> Deck(int sources, bool opt = false, bool sourceEligible = false) => [
        new([Dark, A, B], id: "dark-ab"), new([Dark, B], id: "dark-b"),
        new([A, B], id: "light-ab"), new([B], id: "light-b"),
        new(sourceEligible ? [Dark] : [], sources, "Conditional draw", id: "source", drawCount: 2,
            drawOncePerTurn: opt, postDrawDiscardCount: 1, postDrawSelector: CardSelector.For(Dark))
    ];

    private static List<Combo> Combos() => [
        new([new(A, 1, 0, RequirementMaximumMode.HandSize), new(B, 1, 0, RequirementMaximumMode.HandSize)], groupId: "g"),
        new([], cards: [new("dark-b", 1, 1)], groupId: "g"),
        new([new(Dark, 0, 0)]), new([], cards: [new("light-b", 0, 0)]),
        new([ComboCategory.SameCard([A, B], 1, 1)]),
        new([], alternativeGroups: [new([ComboAlternative.For(new ComboCategory(A, 1, 1)),
            ComboAlternative.For(new ComboCard("source", 1, 2))])]),
        new([], cards: [new("light-ab", 1, 1), new("light-b", 1, 1)])
    ];

    private static IEnumerable<TestCaseData> Cases() {
        foreach (int sources in new[] { 1, 2, 3 }) {
            foreach (int hand in new[] { 0, 1, 2, 4 }) {
                foreach (bool opt in new[] { false, true }) {
                    yield return new(sources, hand, opt);
                }
            }
        }
    }

    [TestCaseSource(nameof(Cases))]
    public void DeferredSubsetsMatchIndependentPhysicalPermutations(int sources, int hand, bool opt) {
        AssertOracle(Deck(sources, opt), hand, Combos());
    }

    [TestCase(false), TestCase(true)]
    public void SourceMayBeRemovalMaterialWithoutLosingOptOrQueueIdentity(bool opt) {
        AssertOracle(Deck(3, opt, true), 2, Combos());
    }

    [Test]
    public void FailureMassIsPreservedAndNoSourceOpeningStillSucceeds() {
        List<Card> deck = Deck(2);
        deck.RemoveAll(card => card.Categories.Contains(Dark));
        List<Combo> combos = [new([], cards: [new("light-ab", 1, 1)]), new([], cards: [new("light-b", 1, 1)])];
        DrawSequenceOracle.Counts oracle = AssertOracle(deck, 2, combos);
        ExactCalculationResult routed = new DrawEffectCalculation(deck, 2, new(CalculationWorkPolicy.Interactive))
            .Calculate(combos, [new("g", "Group"), new("empty", "Empty")]);
        AssertRows(routed, oracle);
        Assert.That(oracle.Failed > 0, Is.True);
        Assert.That(oracle.Wins[0] > 0, Is.True);
        Assert.That(oracle.Wins[0], Is.LessThan(oracle.Total));
    }

    [Test]
    public void DistinctSameNameEntriesStaySeparateAndUnsafeShapesRetainOrderedResolution() {
        List<Card> original = Deck(1);
        Card source = original[^1];
        foreach (Card changed in new[] { source.WithPreDrawRemovalCount(1),
            source.WithEffect(source.EffectPlan with { FirstActivationOnly = true }, source.EffectProfile) }) {
            List<Card> deck = [.. original.Take(original.Count - 1), changed];
            AssertOracle(deck, 2, Combos(), deferred: false);
        }
        original.Add(new([], name: source.Name, id: "source-two", drawCount: 2, drawOncePerTurn: true,
            postDrawDiscardCount: 1, postDrawSelector: CardSelector.For(Dark)));
        AssertOracle(original, 2, Combos());
    }

    [TestCase(1, 0), TestCase(0, 1), TestCase(1, 1)]
    public void HiddenOperationsPreserveFilteredPostRemovalExchangeability(int hiddenCost, int mill) {
        List<Card> deck = Deck(2);
        deck[^1] = deck[^1].WithPreDrawDeckRemovalCount(hiddenCost).WithPostDrawDeckMillCount(mill);
        AssertOracle(deck, 2, Combos());
    }

    [Test]
    public void MandatoryTwoCardRemovalAndCompositeSelectorRemainExact() {
        List<Card> deck = Deck(2);
        deck[^1] = deck[^1].WithPostDrawDiscardCount(2).WithPostDrawSelector(CardSelector.AllOf([Dark.Identity, B.Identity]));
        AssertOracle(deck, 2, Combos());
    }

    [Test]
    public void RemovalUsesAllActiveComboRelevanceForEveryOutput() {
        List<Card> deck = Deck(2);
        deck.Insert(0, new([Dark], id: "irrelevant-dark"));
        List<Combo> combos = [new([], cards: [new("dark-b", 1, 1)], groupId: "g"),
            new([], cards: [new("dark-ab", 1, 1)]), new([], cards: [new("light-ab", 1, 1)]),
            new([new(Dark, 0, 1)], cards: [new("light-b", 1, 1)])];
        AssertOracle(deck, 3, combos);
    }

    [Test]
    public void ZeroCopyEffectsDoNotCreateAnotherActiveEffectKind() {
        List<Card> deck = Deck(2);
        deck.Add(new([], 0, id: "zero", drawCount: 3, preDrawRemovalCount: 1));
        AssertOracle(deck, 2, Combos());
    }

    private static DrawSequenceOracle.Counts AssertOracle(List<Card> deck, int hand, List<Combo> combos, bool deferred = true) {
        List<ComboGroup> groups = [new("g", "Group"), new("empty", "Empty")];
        DrawSequenceOracle.Counts oracle = DrawSequenceOracle.Enumerate(deck, hand, combos, groups, collapseIdenticalCopies: true);
        foreach (List<Card> ordered in new[] { deck, deck.AsEnumerable().Reverse().ToList() }) {
            foreach (bool custom in new[] { false, true }) {
                DrawActivationPriority priority = custom ? new(DrawActivationMode.CustomPriority, ["source-two", "source"])
                    : DrawActivationPriority.Default;
                DrawSequenceOracle.Counts expected = custom
                    ? DrawSequenceOracle.Enumerate(deck, hand, combos, groups, collapseIdenticalCopies: true, priorityIds: priority.CardIds)
                    : oracle;
                PostDrawCalculation calculation = new(ordered, hand, new(CalculationWorkPolicy.Interactive), priority);
                ExactCalculationResult result = calculation.Calculate(combos, groups);
                Assert.That(calculation.DeferredRows > 0, Is.EqualTo(deferred));
                AssertRows(result, expected);
                ExactCalculationResult reference = new PostDrawCalculation(ordered, hand, new(CalculationWorkPolicy.Interactive), priority,
                    deferIdentities: false).Calculate(combos, groups);
                AssertRows(reference, expected);
                ExactCalculationResult total = new PostDrawCalculation(ordered, hand, new(CalculationWorkPolicy.Interactive), priority)
                    .Calculate(combos, groups, totalOnly: true);
                Assert.That(total.Total.Numerator * expected.Total, Is.EqualTo(expected.Wins[0] * total.Total.Denominator));
            }
        }
        return oracle;
    }

    private static void AssertRows(ExactCalculationResult result, DrawSequenceOracle.Counts oracle) {
        ExactProbability[] rows = [result.Total, .. result.Combos, .. result.Groups];
        for (int i = 0; i < rows.Length; i++) {
            Assert.That(rows[i].Numerator * oracle.Total, Is.EqualTo(oracle.Wins[i] * rows[i].Denominator), $"row {i}");
        }
    }

    [TestCase(1), TestCase(3)]
    public async Task RealExampleCompletesAtStandardAndWireModesPreserveSnapshot(int copies) {
        SessionState session = await RealisticAllureFixture.Load(copies);
        WorkBudget budget = new(CalculationWorkPolicy.Interactive);
        PostDrawCalculation calculation = new(session.Cards, 5, budget);
        ExactCalculationResult exact = calculation.Calculate(session.Combos, session.ComboGroups);
        Assert.Multiple(() => {
            Assert.That(calculation.DeferredRows, Is.GreaterThan(1));
            Assert.That(calculation.PeakCells, Is.LessThan(20_000));
            Assert.That(budget.Spent, Is.LessThan(5_000_000));
            Assert.That(DrawExecutionAssessment.Assess(session.Cards, session.Combos, 5, true).Path,
                Is.EqualTo(DrawExecutionPath.DeferredConditional));
        });
        foreach ((bool enabled, bool configured) in new[] { (true, true), (true, false), (false, true) }) {
            List<Card> modeled = configured ? session.Cards : session.Cards.Select(card => card.WithPostDrawDiscardCount(0)).ToList();
            CalculationSnapshot snapshot = CalculationSnapshot.Capture(modeled, session.Combos, 5, session.ComboGroups,
                CalculationWorkPolicy.Interactive, enabled, options: new());
            ProbabilityCalculationResult result = CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json));
            ExactCalculationResult expected = !enabled
                ? new ProbabilityCalculation(session.Cards, 5, new WorkBudget(CalculationWorkPolicy.Interactive)).CalculateExactResults(session.Combos, session.ComboGroups)
                : configured ? exact : new DrawEffectCalculation(modeled, 5, new(CalculationWorkPolicy.Interactive)).Calculate(session.Combos, session.ComboGroups);
            ProbabilityCalculationResult reference = expected.ToPublic(session.Combos, session.ComboGroups, new(CalculationWorkPolicy.Interactive));
            Assert.That(result.TotalProbability, Is.EqualTo(reference.TotalProbability));
            Assert.That(result.ComboProbabilities.Select(row => row.Probability), Is.EqualTo(reference.ComboProbabilities.Select(row => row.Probability)));
            Assert.That(result.GroupProbabilities.Select(row => row.Probability), Is.EqualTo(reference.GroupProbabilities.Select(row => row.Probability)));
            Card beforeEdit = session.Cards[0];
            session.Cards[0] = beforeEdit.WithCopies(beforeEdit.Copies + 1);
            session.Cards.Add(new([], 0, active: false, id: "inactive"));
            Assert.That(CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json)).TotalProbability, Is.EqualTo(result.TotalProbability));
            session.Cards[0] = beforeEdit;
        }
        Assert.That(session.Cards.Single(card => card.Id == "benchmark-allure").PostDrawDiscardCount, Is.EqualTo(1));
    }
}
