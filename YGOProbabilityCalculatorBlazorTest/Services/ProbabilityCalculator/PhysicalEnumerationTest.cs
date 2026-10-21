using System.Numerics;
using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture, NonParallelizable]
public sealed class PhysicalEnumerationTest {
    [TestCase(false), TestCase(true)]
    public void ForcedExplicitRouteAgreesWithIndependentLabeledOracleForAllFixtures(bool frontier) {
        int count = 0;
        foreach (SearchCertificateFixtures.Fixture fixture in SearchCertificateFixtures.Fixtures()) {
            List<Card> cards = fixture.Deck.Where(card => card.Active && card.Copies > 0).ToList();
            WorkBudget budget = new(CalculationWorkPolicy.Default);
            DrawActivationPriority? priority = fixture.Priority is null ? null : new(DrawActivationMode.CustomPriority, fixture.Priority);
            PostDrawCalculation execution = new(cards, fixture.Hand, budget, priority);
            ExactCalculationResult result = execution.CalculateSearch(fixture.Combos, fixture.Groups, false, new(cards, budget), physical: true,
                method: frontier ? PhysicalEnumerationMethod.Frontier : PhysicalEnumerationMethod.Streaming);
            SearchCertificateOracle.Counts oracle = SearchCertificateOracle.Enumerate(fixture.Deck, fixture.Hand, fixture.Combos, fixture.Groups, fixture.Priority);
            ExactProbability[] rows = [result.Total, .. result.Combos, .. result.Groups];
            for (int row = 0; row < rows.Length; row++) {
                Assert.That(rows[row].Numerator * oracle.Outcomes, Is.EqualTo(oracle.Wins[1][row] * rows[row].Denominator), $"{fixture.Name}/{row}");
                Assert.That(rows[row].Numerator * oracle.Outcomes, Is.LessThanOrEqualTo(oracle.Wins[2][row] * rows[row].Denominator), $"{fixture.Name}/{row}/optimistic");
            }
            count++;
        }
        TestContext.Out.WriteLine($"All {count} systematic and seeded fixtures: every combo, group and total matched.");
    }

    [TestCase(false, false), TestCase(false, true), TestCase(true, false), TestCase(true, true)]
    public void HiddenLossWithOrderedLocksCostsAndIndependentEntryOpt(bool first, bool custom) {
        CategoryBase target = new("Target");
        List<Card> deck = [new([], 1, "same", id: "s", search: new(CardSelector.Any, false), oncePerTurn: true),
            new([], 1, "same", id: "z", search: new(CardSelector.For(target), false), oncePerTurn: true),
            new([target], 2, id: "aa"), new([target], 1, id: "zz"),
            new([], 1, id: "d", drawCount: 1, firstActivationOnly: first, blocksLaterDraws: !first,
                postDrawDiscardCount: 1, postDrawSelector: CardSelector.For(target), preDrawDeckRemovalCount: 1),
            new([target], 1, id: "e", drawCount: 1, preDrawRemovalCount: 1, preDrawSelector: CardSelector.For(target), postDrawDeckMillCount: 1)];
        List<Combo> combos = [new([new(target, 1, 2)], "One or two", groupId: "g"), new([new(target, 0, 0)], "None", groupId: "g"),
            new([], "No draw", cards: [new("e", 0, 0)])];
        List<ComboGroup> groups = [new("g", "Union"), new("empty", "Empty")];
        string[]? priority = custom ? ["e", "d"] : null;
        SearchCertificateOracle.Counts oracle = SearchCertificateOracle.Enumerate(deck, 3, combos, groups, priority);
        ProbabilityCalculationResult result = new ProbabilityCalculatorService().CalculateProbabilityResults(deck, combos, 3, groups,
            CalculationWorkPolicy.Default, new(priority is null ? null : new(DrawActivationMode.CustomPriority, priority), Mode: CalculationModel.Exact));
        PhysicalSearchTest.AssertRows(result, oracle.Wins[1], oracle.Outcomes);
        Assert.That(result.Contract!.SolverRoute, Is.EqualTo("SearchPhysicalFrontier"));
        TestContext.Out.WriteLine($"first={first} custom={custom}: [{string.Join(',', oracle.Wins[1])}]/{oracle.Outcomes}, failed={oracle.Failed}");
    }

    [TestCase(false), TestCase(true)]
    public void LimitedFallbackPublishesNoRowsThenWorkerRecovers(bool storage) {
        List<Card> deck = ThreeCalculationModesTest.Counterexample("discard");
        List<ComboGroup> groups = storage ? Enumerable.Range(0, 32768).Select(index => new ComboGroup("g" + index, "Group")).ToList() : [];
        CalculationSnapshot limited = CalculationSnapshot.Capture(deck, ThreeCalculationModesTest.Combos, 2, groups,
            storage ? CalculationWorkPolicy.Default : new(500), options: new(Mode: CalculationModel.Exact));
        CalculationResponse response = JsonSerializer.Deserialize(CalculationWire.Execute(limited.Json), CalculationJsonContext.Default.CalculationResponse)!;
        Assert.That(response.Result, Is.Null);
        Assert.That(response.Contract!.Fractions, Is.Null);
        Assert.That(response.Contract.Completion, Is.EqualTo(storage ? CalculationCompletion.StorageLimit : CalculationCompletion.WorkLimit));
        ProbabilityCalculationResult recovered = ThreeCalculationModesTest.Run(deck, 2, CalculationModel.Exact);
        Assert.That(recovered.Contract!.Fractions!.Combos[0], Is.EqualTo(new ProbabilityFraction("1", "2")));
    }

    [Test]
    public void ManyObservationallyEqualEntriesCompleteWithinOriginalLimits() {
        CategoryBase target = new("Target");
        List<Card> deck = Enumerable.Range(0, 38).Select(index => new Card([target], 1, id: "t" + index)).ToList();
        deck.Add(new([], 1, id: "s", search: new(CardSelector.For(target), false)));
        deck.Add(new([], 1, id: "d", drawCount: 1, postDrawDiscardCount: 1));
        WorkBudget budget = new(CalculationWorkPolicy.Default);
        DrawEffectCalculation execution = new(deck, 5, budget, physicalSearch: true);
        ExactCalculationResult result = execution.Calculate([new([new(target, 1, 6)], "Target")], []);
        // Every five-card opening contains at least three targets. The only
        // draw can remove at most one; declining the optional search preserves
        // a target. This bound is independent of the row quotient.
        Assert.That(result.Total.ToFraction(), Is.EqualTo(new ProbabilityFraction("1", "1")));
        Assert.That(result.Combos.Single().ToFraction(), Is.EqualTo(result.Total.ToFraction()));
        Assert.That(budget.Spent, Is.LessThan(CalculationWorkPolicy.Default.WorkUnits));
        Assert.That(budget.PeakCells, Is.LessThanOrEqualTo(CalculationStoragePolicy.Standard.CellLimit));
    }
}
