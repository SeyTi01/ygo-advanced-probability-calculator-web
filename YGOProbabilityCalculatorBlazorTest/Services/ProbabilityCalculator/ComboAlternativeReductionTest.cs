using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Counting;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Compilation;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public class ComboAlternativeReductionTest {
    private static readonly CategoryBase A = new("A");
    private static readonly CategoryBase B = new("B");
    private static readonly CategoryBase MetadataA = new("A", CategorySource.Metadata, "attribute:fire");

    private static ComboAlternative Cat(
        CategoryBase category,
        int min = 1,
        int max = 0,
        RequirementMaximumMode mode = RequirementMaximumMode.HandSize
    ) => ComboAlternative.For(new ComboCategory(category, min, max, mode));

    private static Combo Repeated(int count, params ComboAlternative[] alternatives) => new([], groupId: "g",
        alternativeGroups: Enumerable.Range(0, count).Select(index => new ComboAlternativeGroup(
            index % 2 == 0 ? alternatives : alternatives.Reverse().ToArray())));

    private static List<Card> Deck() => [new([A, B, MetadataA], 2, id: "ab"), new([A], id: "a"),
        new([B], id: "b"), new([], 2, id: "blank"), new([A], 0, id: "zero")];

    [Test, Explicit("Compile-only diagnostics; separate from timed frozen-input Replay")]
    public void InspectFrozenRouteReduction() {
        System.Text.Json.JsonElement[] fixtures = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement[]>(
            File.ReadAllText(Environment.GetEnvironmentVariable("YGO_AUDIT_INPUT")!))!;
        foreach (System.Text.Json.JsonElement fixture in fixtures) {
            CalculationInput input = System.Text.Json.JsonSerializer.Deserialize(fixture.GetProperty("Input"), CalculationJsonContext.Default.CalculationInput)!;
            List<Card> deck = input.Cards.Select(card => new Card(card.Categories, card.Copies, id: card.Id)).ToList();
            foreach (WorkerCombo comboInput in input.Combos) {
                Combo combo = new(comboInput.Categories, cards: comboInput.Cards, alternativeGroups: comboInput.AlternativeGroups);
                if (combo.AlternativeGroups.Count == 0) {
                    continue;
                }
                WorkBudget preprocessing = new(CalculationWorkPolicy.Interactive);
                IReadOnlyList<ComboAlternativeGroup> reduced = ComboAlternativeReducer.Reduce(combo.AlternativeGroups, preprocessing);
                BigInteger raw = combo.AlternativeGroups.Aggregate(BigInteger.One, (product, group) => product * group.Alternatives.Count);
                BigInteger retained = reduced.Aggregate(BigInteger.One, (product, group) => product * group.Alternatives.Count);
                WorkBudget compilation = new(CalculationWorkPolicy.Interactive);
                string outcome;
                try {
                    outcome = new ComboEventCompiler(deck, input.HandSize, compilation).CompileCombo(combo).Count.ToString();
                }
                catch (ProbabilityCalculationLimitException error) {
                    outcome = error.Reason.ToString();
                }
                TestContext.Out.WriteLine($"{fixture.GetProperty("Name").GetString()}: raw={raw}, retained={retained}, preprocessWork={preprocessing.Spent}, compiledEvents={outcome}, compileWork={compilation.Spent}");
            }
        }
    }

    [TestCase(1), TestCase(2), TestCase(4), TestCase(8)]
    public void RepeatedBinaryGroupsMatchIndependentPhysicalHands(int count) {
        VerifyHands(Deck(), [Repeated(count, Cat(A), Cat(B)), new([new(A, 0, 0)], groupId: "g")]);
    }

    [TestCase(1), TestCase(2), TestCase(4)]
    public void RepeatedBoundedAndMixedRolesMatchIndependentPhysicalHands(int count) {
        ComboAlternative and = ComboAlternative.For(ComboCategory.SameCard([B, A], 1, 0, RequirementMaximumMode.HandSize));
        ComboAlternative direct = ComboAlternative.For(new ComboCard("ab", 2, 2));
        List<Combo> combos = [
            Repeated(count, Cat(A, 2, 2, RequirementMaximumMode.Fixed), Cat(B, 0, 0, RequirementMaximumMode.Fixed)),
            Repeated(count, direct, Cat(MetadataA)).WithCategories([new(A, 1, 0, RequirementMaximumMode.HandSize)]),
            Repeated(count, and, Cat(B)).WithCategories([new(A, 1, 0, RequirementMaximumMode.HandSize)]),
            Repeated(count, Cat(A, 0), Cat(B)).WithCards([new("ab", 1, 0, RequirementMaximumMode.HandSize)]),
            Repeated(count, Cat(A, 0, 0, RequirementMaximumMode.Fixed), direct).WithCategories([new(A, 1, 1)]),
            Repeated(count, ComboAlternative.For(new ComboCard("missing", 1, 1)), ComboAlternative.For(new ComboCard("zero", 1, 1))),
            Repeated(count, Cat(A), Cat(MetadataA)),
            Repeated(count, and, ComboAlternative.For(ComboCategory.SameCard([B, MetadataA], 2, 3)))
        ];
        VerifyHands(Deck(), combos);
    }

    [Test]
    public void BoundsModesKindsAndStableIdentitiesKeepDifferentGroups() {
        ComboAlternative first = Cat(A, 1, 2, RequirementMaximumMode.Fixed);
        ComboAlternative[] different = [Cat(A, 2, 2, RequirementMaximumMode.Fixed), Cat(A, 1, 3, RequirementMaximumMode.Fixed),
            Cat(A, 1, 2), Cat(MetadataA, 1, 2, RequirementMaximumMode.Fixed),
            ComboAlternative.For(new ComboCard("ab", 1, 2)), ComboAlternative.For(new ComboCard("a", 1, 2)),
            ComboAlternative.For(ComboCategory.SameCard([A, B], 1, 2))];
        foreach (ComboAlternative other in different) {
            Combo combo = new([], alternativeGroups: [new([first, Cat(B)]), new([other, Cat(B)])]);
            Assert.That(ComboAlternativeReducer.Reduce(combo.AlternativeGroups, new(CalculationWorkPolicy.Interactive)), Has.Count.EqualTo(2));
            VerifyHands(Deck(), [combo]);
        }
    }

    [Test]
    public void PermutedGroupsAndDuplicateAlternativesReduceWithoutChangingInput() {
        ComboAlternative and = ComboAlternative.For(ComboCategory.SameCard([A, B], 1, 3));
        ComboAlternative reversed = ComboAlternative.For(ComboCategory.SameCard([B, A], 1, 3));
        Combo combo = new([], alternativeGroups: [new([and, Cat(B), and]), new([Cat(B), reversed])]);
        IReadOnlyList<ComboAlternativeGroup> reduced = ComboAlternativeReducer.Reduce(combo.AlternativeGroups, new(CalculationWorkPolicy.Interactive));
        Assert.That(reduced, Has.Count.EqualTo(1));
        Assert.That(reduced[0].Alternatives, Has.Count.EqualTo(2));
        Assert.That(combo.AlternativeGroups[0].Alternatives, Has.Count.EqualTo(3));
        VerifyHands(Deck(), [combo]);
    }

    [TestCase(16), TestCase(64)]
    public void FormerlyRejectedRedundantProductHasExactIndependentExpectation(int count) {
        Combo combo = Repeated(count, Cat(A), Cat(B));
        // Independent physical oracle for one group; the repeated-choice proof
        // extends this expectation without making the oracle enumerate 2^64.
        (int Successes, int Total) expected = SmallDeckOracle.EnumerateCounts(Deck(), [Repeated(1, Cat(A), Cat(B))], 2);
        foreach (CalculationStorageTier tier in Enum.GetValues<CalculationStorageTier>()) {
            WorkBudget budget = new(CalculationWorkPolicy.Interactive, CalculationStoragePolicy.ForTier(tier));
            AssertFraction(new ProbabilityCalculation(Deck(), 2, budget).CalculateUnionExact([combo]), expected);
            Assert.That(new ComboEventCompiler(Deck(), 2, budget).CompileCombo(combo), Has.Count.EqualTo(2));
        }
    }

    [Test]
    public void NonEquivalentProductAndPreprocessingRetainTypedLimits() {
        Combo distinct = new([], alternativeGroups: Enumerable.Range(0, 16).Select(index =>
            new ComboAlternativeGroup([Cat(A), ComboAlternative.For(new ComboCard($"id{index}", 1, 1))])));
        ProbabilityCalculationLimitException? product = Assert.Throws<ProbabilityCalculationLimitException>(() =>
            new ComboEventCompiler(Deck(), 2, new(CalculationWorkPolicy.Interactive)).CompileCombo(distinct));
        Assert.That(product!.Reason, Is.EqualTo(ProbabilityCalculationLimitReason.Storage));
        Combo longKey = Repeated(2, ComboAlternative.For(new ComboCard(new string('x', 270000), 1, 1)));
        ProbabilityCalculationLimitException? storage = Assert.Throws<ProbabilityCalculationLimitException>(() =>
            ComboAlternativeReducer.Reduce(longKey.AlternativeGroups, new(CalculationWorkPolicy.Interactive)));
        Assert.That(storage!.Reason, Is.EqualTo(ProbabilityCalculationLimitReason.Storage));
        foreach (CalculationStorageTier tier in Enum.GetValues<CalculationStorageTier>()) {
            ProbabilityCalculationLimitException? work = Assert.Throws<ProbabilityCalculationLimitException>(() =>
                ComboAlternativeReducer.Reduce(Repeated(16, Cat(A), Cat(B)).AlternativeGroups, new(new(10), CalculationStoragePolicy.ForTier(tier))));
            Assert.That(work!.Reason, Is.EqualTo(ProbabilityCalculationLimitReason.Work));
        }
    }

    [TestCase(false), TestCase(true)]
    public void DrawPathsMatchIndependentLabeledPermutations(bool advanced) {
        List<Card> deck = [new([], id: "a-deferred", drawCount: 1, preDrawRemovalCount: advanced ? 1 : 0,
                preDrawSelector: advanced ? CardSelector.For(B) : CardSelector.Any),
            new([], id: "b-draw", drawCount: 2, postDrawDiscardCount: advanced ? 1 : 0),
            new([A, B], id: "ab"), new([A], id: "a"), new([B], id: "b"), new([], id: "blank")];
        List<Combo> combos = [Repeated(4, Cat(A), ComboAlternative.For(ComboCategory.SameCard([A, B], 1, 0, RequirementMaximumMode.HandSize))),
            Repeated(3, Cat(B), ComboAlternative.For(new ComboCard("a", 1, 2))), new([new(A, 0, 0)])];
        List<ComboGroup> groups = [new("g", "Group")];
        foreach (int hand in new[] { 2, 3, 5 }) {
            DrawSequenceOracle.Counts expected = DrawSequenceOracle.Enumerate(deck, hand, combos, groups);
            foreach (List<Card> order in new[] { deck, deck.AsEnumerable().Reverse().ToList() }) {
                WorkBudget budget = new(CalculationWorkPolicy.Interactive);
                ExactCalculationResult actual = new DrawEffectCalculation(order, hand, budget).Calculate(combos, groups);
                ExactProbability[] values = [actual.Total, .. actual.Combos, .. actual.Groups];
                for (int index = 0; index < values.Length; index++) {
                    Assert.That(values[index].Numerator * expected.Total, Is.EqualTo(expected.Wins[index] * values[index].Denominator));
                }
            }
        }
    }

    [Test]
    public void SnapshotFiltersInactiveInputsBeforeReducingGroups() {
        List<Card> deck = Deck();
        deck.Add(new([A], 20, active: false, id: "inactive"));
        List<Combo> combos = [Repeated(16, Cat(A), Cat(B)), new([], active: false)];
        CalculationSnapshot snapshot = CalculationSnapshot.Capture(deck, combos, 2, [new("g", "Group")]);
        ProbabilityCalculationResult result = CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json));
        (int Successes, int Total) expected = SmallDeckOracle.EnumerateCounts(Deck(), [Repeated(1, Cat(A), Cat(B))], 2);
        Assert.That(result.TotalProbability, Is.EqualTo((double)expected.Successes / expected.Total).Within(1e-14));
        Assert.That(result.ComboProbabilities, Has.Count.EqualTo(1));
        Assert.That(result.GroupProbabilities![0].Probability, Is.EqualTo(result.TotalProbability));
    }

    private static void VerifyHands(List<Card> deck, List<Combo> combos) {
        for (int hand = 0; hand <= deck.Sum(card => card.Copies); hand++) {
            foreach (List<Card> order in new[] { deck, deck.AsEnumerable().Reverse().ToList() }) {
                List<Combo> permuted = combos.Select(combo => combo.WithAlternativeGroups(combo.AlternativeGroups.AsEnumerable().Reverse())).ToList();
                ExactCalculationResult actual = new ProbabilityCalculation(order, hand, CalculationWorkPolicy.Interactive)
                    .CalculateExactResults(permuted, [new("g", "Group")]);
                AssertFraction(actual.Total, SmallDeckOracle.EnumerateCounts(deck, combos, hand));
                AssertFraction(actual.Groups[0], SmallDeckOracle.EnumerateCounts(deck, combos.Where(combo => combo.GroupId == "g").ToList(), hand));
                for (int index = 0; index < combos.Count; index++) {
                    AssertFraction(actual.Combos[index], SmallDeckOracle.EnumerateCounts(deck, [combos[index]], hand));
                }
            }
        }
    }

    private static void AssertFraction(ExactProbability actual, (int Successes, int Total) expected) {
        Assert.That(actual.Numerator * expected.Total, Is.EqualTo(new BigInteger(expected.Successes) * actual.Denominator));
    }
}
