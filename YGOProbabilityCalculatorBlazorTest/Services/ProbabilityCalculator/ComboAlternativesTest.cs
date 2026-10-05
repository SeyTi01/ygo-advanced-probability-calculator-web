using System.Diagnostics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public class ComboAlternativesTest
{
    private static readonly CategoryBase Fire = new("Fire");
    private static readonly CategoryBase Dark = new("Dark");

    internal static ComboAlternative Cat(
        CategoryBase c,
        int min = 1,
        int max = 5,
        RequirementMaximumMode mode = RequirementMaximumMode.HandSize) =>
        ComboAlternative.For(new ComboCategory(c, min, max, mode));

    internal static ComboAlternative Direct(string id, int min = 1) =>
        ComboAlternative.For(new ComboCard(id, min, 5, RequirementMaximumMode.HandSize));

    internal static Combo Or(params ComboAlternative[] alternatives) => new([], alternativeGroups: [new(alternatives)]);

    private static List<Card> Deck() =>
        [new([Fire], id: "R"), new([Fire], id: "F"), new([Dark], id: "D"), new([], id: "X")];

    [Test, Explicit("Small Release parity/overhead measurements, not a speed claim.")]
    public void MeasureExplicitOrOverhead()
    {
        ProbabilityCalculatorService service = new();
        List<Card> deck = Deck();
        Combo grouped = Or(Cat(Fire), Cat(Dark)).WithCards([new("R", 1, 5, RequirementMaximumMode.HandSize)]);
        List<Combo> repeated =
            [new([new(Fire, 1, 5)], cards: grouped.Cards), new([new(Dark, 1, 5)], cards: grouped.Cards)];

        foreach ((string name, List<Combo> combos) in new[]
                     { ("Repeated routes", repeated), ("Explicit OR", new List<Combo> { grouped }) })
        {
            for (int warmup = 0; warmup < 5; warmup++)
            {
                service.CalculateProbabilityResults(deck, combos, 2);
            }

            List<double> times = [];
            List<long> bytes = [];

            for (int i = 0; i < 11; i++)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                Stopwatch timer = Stopwatch.StartNew();
                ProbabilityCalculationResult result = service.CalculateProbabilityResults(deck, combos, 2);
                timer.Stop();
                times.Add(timer.Elapsed.TotalMilliseconds);
                bytes.Add(GC.GetAllocatedBytesForCurrentThread() - before);
                Assert.That(result.TotalProbability, Is.EqualTo(2d / 6).Within(1e-12));
            }

            TestContext.Out.WriteLine(
                $"{name}: median {times.Order().ElementAt(5):F4} ms, {bytes.Order().ElementAt(5)} allocated bytes");
        }
    }

    [TestCase(1, 0, 4)]
    [TestCase(2, 2, 6)]
    [TestCase(3, 3, 4)]
    public void DirectAndGroupedCategoriesUseDistinctCopiesAndExactUnion(int hand, int success, int total)
    {
        Combo combo = Or(Cat(Fire), Cat(Dark)).WithCards([new("R", 1, 5, RequirementMaximumMode.HandSize)]);
        Verify(Deck(), [combo], hand, success, total);
    }

    [TestCase(2, 1, 6)]
    [TestCase(3, 2, 4)]
    public void UnselectedMaximumDoesNotRestrictOtherRoute(int hand, int success, int total)
    {
        Combo combo = Or(Cat(Fire, 1, 1, RequirementMaximumMode.Fixed), Cat(Dark))
            .WithCards([new("R", 1, 5, RequirementMaximumMode.HandSize)]);
        Verify(Deck(), [combo], hand, success, total);
    }

    [Test]
    public void TwoCopiesMustComeFromOneAlternative() => Verify(
        [new([Fire], 2), new([Dark], 2)],
        [Or(Cat(Fire, 2, 2, RequirementMaximumMode.Fixed), Cat(Dark, 2, 2, RequirementMaximumMode.Fixed))],
        2,
        2,
        6);

    [Test]
    public void ZeroBoundsAreAUnionOfExclusions() => Verify([new([Fire]), new([Dark]), new([])],
        [Or(Cat(Fire, 0, 0, RequirementMaximumMode.Fixed), Cat(Dark, 0, 0, RequirementMaximumMode.Fixed))],
        2,
        2,
        3);

    [Test]
    public void OverlappingBoundedAlternativesNeedSignedInclusionExclusion() => Verify(Deck(),
        [Or(Cat(Fire, 1, 1, RequirementMaximumMode.Fixed), Cat(Dark, 1, 1, RequirementMaximumMode.Fixed))],
        2,
        5,
        6);

    [Test]
    public void MultipleMixedGroupsRepeatedKeysAndImpossibleBranchesMatchOracle()
    {
        CategoryBase metadata = new("Fire", CategorySource.Metadata, "attribute:fire");
        List<Card> deck =
        [
            new([Fire, metadata], 2, "same", id: "a", manualMetadataCategoryKeys: ["attribute:fire"]),
            new([Dark, Fire], 1, "same", id: "b"), new([Dark], id: "c"), new([], id: "x")
        ];
        List<Combo> combos =
        [
            Or(Direct("a"), Direct("b"), Cat(metadata)),
            Or(Cat(Fire), Cat(Dark), Cat(Fire)).WithCategories([new(Fire, 1, 2)]),
            new([], alternativeGroups: [new([Cat(Fire), Direct("b")]), new([Cat(Dark), Direct("a"), Cat(metadata)])]),
            Or(Cat(Fire, 4), Cat(Dark)),
            Or(Cat(Fire, 0, 0, RequirementMaximumMode.Fixed), Cat(Dark)).WithCategories([new(Fire, 1, 2)]),
            Or(Cat(Fire, 0, 1, RequirementMaximumMode.Fixed), Cat(metadata, 2)),
            Or(Direct("a"), Direct("b")).WithCards([new("a", 1, 1)])
        ];

        for (int hand = 1; hand <= 4; hand++)
        {
            Verify(deck, combos, hand);
        }
    }

    [Test]
    public void LogicalOwnershipAndGroupsSurviveMoreThanThirtyInternalRoutes()
    {
        CategoryBase[] categories = [.. Enumerable.Range(0, 36).Select(i => new CategoryBase($"C{i}"))];
        List<Card> deck = [.. categories.Select(c => new Card([c])), new Card([])];
        Combo combo = Or([.. categories.Select(c => Cat(c))]).WithName("Many alternatives").WithGroup("g");
        Combo second = Or(Cat(categories[0]), Cat(categories[1])).WithName("Overlap").WithGroup("g");
        ProbabilityCalculationResult result =
            new ProbabilityCalculatorService().CalculateProbabilityResults(deck,
                [combo, second],
                1,
                [new("g", "Group")]);
        Assert.Multiple(() =>
        {
            Assert.That(result.TotalProbability, Is.EqualTo(36d / 37).Within(1e-12));
            Assert.That(result.ComboProbabilities.Select(c => c.ComboName),
                Is.EqualTo(new[] { "Many alternatives", "Overlap" }));
            Assert.That(result.ComboProbabilities.Select(c => c.Probability),
                Is.EqualTo(new[] { 36d / 37, 2d / 37 }).Within(1e-12));
            Assert.That(result.GroupProbabilities![0].ActiveComboCount, Is.EqualTo(2));
            Assert.That(result.GroupProbabilities[0].Probability, Is.EqualTo(36d / 37).Within(1e-12));
        });
    }

    [Test]
    public void ExpansionAndSharedWorkBudgetFailExplicitlyThenNextRequestSucceeds()
    {
        Combo large = new([],
            alternativeGroups: Enumerable.Range(0, 16).Select(_ => new ComboAlternativeGroup([Cat(Fire), Cat(Dark)])));
        ProbabilityCalculatorService service = new();
        Assert.Throws<ProbabilityCalculationLimitException>(() =>
            service.CalculateProbabilityResults(Deck(), [large], 2));
        Assert.Throws<ProbabilityCalculationLimitException>(() =>
            service.CalculateProbabilityResults(Deck(), [Or(Cat(Fire), Cat(Dark))], 2, null, new(1)));
        Verify(Deck(), [Or(Cat(Fire), Cat(Dark))], 2, 6, 6);
    }

    [Test]
    public void WorkerOwnsFrozenAlternativesAndReconstructsThem()
    {
        Combo combo = Or(Cat(Fire), Cat(Dark)).WithCards([new("R", 1, 5, RequirementMaximumMode.HandSize)]);
        CalculationSnapshot snapshot = CalculationSnapshot.Capture(Deck(), [combo], 2, []);
        combo.AlternativeGroups.Clear();
        ProbabilityCalculationResult result = CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json));
        Assert.That(result.TotalProbability, Is.EqualTo(2d / 6).Within(1e-12));
        Assert.That(result.ComboProbabilities, Has.Count.EqualTo(1));
    }

    private static void Verify(List<Card> deck, List<Combo> combos, int hand, int? success = null, int? total = null)
    {
        (int Successes, int Total) expected = SmallDeckOracle.EnumerateCounts(deck, combos, hand);

        if (success is not null)
        {
            Assert.That(expected,
                Is.EqualTo((success.Value, total!.Value)),
                "Independently enumerate the stated physical hands.");
        }

        ProbabilityCalculationResult result =
            new ProbabilityCalculatorService().CalculateProbabilityResults(deck, combos, hand);
        Assert.That(result.TotalProbability, Is.EqualTo((double)expected.Successes / expected.Total).Within(1e-12));

        for (int i = 0; i < combos.Count; i++)
        {
            Assert.That(result.ComboProbabilities[i].Probability,
                Is.EqualTo(SmallDeckOracle.EnumerateProbability(deck, [combos[i]], hand)).Within(1e-12));
        }
    }
}
