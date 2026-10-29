using System.Text.Json;
using System.Text.Json.Nodes;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture, NonParallelizable]
public sealed class AdaptiveFastRouteTest {
    [Test]
    public void SelectedRequestsMatchEveryIndependentPhysicalRowAndExactMode() {
        int selected = 0;
        foreach (SearchCertificateFixtures.Fixture fixture in SearchCertificateFixtures.Fixtures()) {
            CalculationOptions options = new(fixture.Priority is null ? null : new(DrawActivationMode.CustomPriority, fixture.Priority));
            ProbabilityCalculationResult fast = new ProbabilityCalculatorService().CalculateProbabilityResults(
                fixture.Deck, fixture.Combos, fixture.Hand, fixture.Groups, CalculationWorkPolicy.Default, options);
            ProbabilityCalculationResult wire = CalculationWire.ReadResult(CalculationWire.Execute(CalculationSnapshot.Capture(
                fixture.Deck, fixture.Combos, fixture.Hand, fixture.Groups, options: options).Json), CalculationModel.Fast);
            Assert.That(JsonSerializer.Serialize(wire), Is.EqualTo(JsonSerializer.Serialize(fast)), fixture.Name);
            if (!fast.Contract!.SolverRoute.StartsWith("SearchPhysical", StringComparison.Ordinal)) {
                AssertUnchanged(fixture.Deck, fixture.Combos, fixture.Hand, fixture.Groups, priority: options.DrawActivationPriority);
                continue;
            }
            selected++;
            Assert.That(fast.Contract.RequestedMode, Is.EqualTo(CalculationModel.Fast));
            Assert.That(fast.Contract.ExecutedMode, Is.EqualTo(CalculationModel.Fast));
            Assert.That(fast.Contract.Accuracy, Is.EqualTo(CalculationAccuracy.Exact));
            Assert.That(fast.Contract.Reference, Is.EqualTo(CalculationResultContract.EffectsReference));
            Assert.That(fast.Contract.Proof, Is.Not.Empty);
            Assert.That(fast.SearchAvailability, Is.Null);
            ProbabilityCalculationResult exact = new ProbabilityCalculatorService().CalculateProbabilityResults(
                fixture.Deck, fixture.Combos, fixture.Hand, fixture.Groups, CalculationWorkPolicy.Default, options with { Mode = CalculationModel.Exact });
            Assert.That(JsonSerializer.Serialize(fast.Contract.Fractions), Is.EqualTo(JsonSerializer.Serialize(exact.Contract!.Fractions)), fixture.Name);
            SearchCertificateOracle.Counts oracle = SearchCertificateOracle.Enumerate(fixture.Deck, fixture.Hand,
                fixture.Combos, fixture.Groups, fixture.Priority);
            PhysicalSearchTest.AssertRows(fast, oracle.Wins[1], oracle.Outcomes);
        }
        Assert.That(selected, Is.GreaterThanOrEqualTo(20));
        TestContext.Out.WriteLine($"{selected} adaptive requests matched every labeled physical total/combo/group row.");
    }

    [Test]
    public void FilteredDiscardChangesTheFractionAndExplicitScalarPolicyWithoutRelabelingTheOldUpper() {
        SearchCertificateFixtures.Fixture fixture = SearchCertificateFixtures.Fixtures().Single(value => value.Name == "minimal-filtered-target-discard");
        ProbabilityCalculatorService engine = new();
        ProbabilityCalculationResult fast = engine.CalculateProbabilityResults(fixture.Deck, fixture.Combos, fixture.Hand, fixture.Groups);
        Assert.That(fast.Contract!.Fractions!.Total, Is.EqualTo(new ProbabilityFraction("1", "2")));
        Assert.That(fast.Contract.Fractions.Combos[0], Is.EqualTo(new ProbabilityFraction("1", "2")));
        Assert.That(engine.CalculateProbabilityForCombos(fixture.Deck, fixture.Combos, fixture.Hand,
            CalculationWorkPolicy.Default, new(Mode: CalculationModel.Fast)), Is.EqualTo(0.5));
        Assert.That(engine.CalculateProbabilityForCombos(fixture.Deck, fixture.Combos, fixture.Hand), Is.EqualTo(17d / 30));
    }

    [TestCase(8), TestCase(9)]
    public void FortyCardDistinctEntryControlsKeepTheOldRouteAndCounters(int entries) {
        List<Card> cards = ThreeCalculationModesTest.Counterexample("discard");
        while (cards.Count < entries) {
            cards.Add(new([], 1, id: "blank" + cards.Count));
        }
        cards[2] = cards[2].WithCopies(40 - cards.Where((_, row) => row != 2).Sum(card => card.Copies));
        AssertUnchanged(cards, ThreeCalculationModesTest.Combos, 5, []);
    }

    [TestCase(false), TestCase(true)]
    public void DuplicateDrawCopiesKeepPerEntryOptAndFailedBranches(bool opt) {
        CategoryBase target = new("Target");
        List<Card> cards = [new([], 1, id: "s", search: new(CardSelector.For(target), false)),
            new([target], 1, id: "t"), new([], 1, id: "f"),
            new([], 2, id: "d", drawCount: 1, drawOncePerTurn: opt,
                postDrawDiscardCount: 1, postDrawSelector: CardSelector.For(target))];
        List<Combo> combos = [new([new(target, 1, 2)], groupId: "g"), new([new(target, 0, 0)])];
        List<ComboGroup> groups = [new("g", "Positive")];
        ProbabilityCalculationResult result = new ProbabilityCalculatorService().CalculateProbabilityResults(cards, combos, 2, groups);
        Assert.That(result.Contract!.SolverRoute, Is.EqualTo("SearchPhysicalDeferred"));
        SearchCertificateOracle.Counts oracle = SearchCertificateOracle.Enumerate(cards, 2, combos, groups);
        PhysicalSearchTest.AssertRows(result, oracle.Wins[1], oracle.Outcomes);
        Assert.That(oracle.Failed, Is.GreaterThan(System.Numerics.BigInteger.Zero));
    }

    [Test]
    public void AdaptiveWireStillRejectsMissingProofAndContradictoryModeOrAccuracy() {
        JsonObject response = JsonNode.Parse(CalculationWire.Execute(CalculationSnapshot.Capture(
            ThreeCalculationModesTest.Counterexample("discard"), ThreeCalculationModesTest.Combos, 2,
            ThreeCalculationModesTest.Groups).Json))!.AsObject();
        foreach ((string field, JsonNode? value) in new (string, JsonNode?)[] {
            ("Proof", null), ("ExecutedMode", JsonValue.Create(2)), ("Accuracy", JsonValue.Create(1))
        }) {
            JsonObject corrupt = response.DeepClone().AsObject();
            corrupt["Result"]!["Contract"]![field] = value;
            Assert.Throws<CalculationProtocolException>(() => CalculationWire.ReadResult(corrupt.ToJsonString(), CalculationModel.Fast), field);
        }
    }

    [Test]
    public void LowBudgetsBranchingAndLargeMultiplicityStayOnTheHistoricalPath() {
        SearchCertificateFixtures.Fixture fixture = SearchCertificateFixtures.Fixtures().Single(value => value.Name == "minimal-filtered-target-discard");
        AssertUnchanged(fixture.Deck, fixture.Combos, fixture.Hand, fixture.Groups, new(10000));
        AssertUnchanged(fixture.Deck, fixture.Combos, fixture.Hand,
            Enumerable.Range(0, 10).Select(index => new ComboGroup("g" + index, "Group")).ToList());
        List<Card> copies = fixture.Deck.Select(card => card.Id == "f" ? card.WithCopies(37) : card).ToList();
        AssertUnchanged(copies, fixture.Combos, fixture.Hand, fixture.Groups);
        List<Card> searchers = [.. fixture.Deck, new([], 1, id: "z", search: fixture.Deck[0].Search)];
        AssertUnchanged(searchers, fixture.Combos, fixture.Hand, fixture.Groups);
        List<Combo> alternatives = [new([], alternativeGroups: [new(Enumerable.Repeat(
            ComboAlternative.For(fixture.Combos[0].Categories[0]), 8).ToArray())])];
        AssertUnchanged(fixture.Deck, alternatives, fixture.Hand, fixture.Groups);
    }

    private static void AssertUnchanged(List<Card> cards, List<Combo> combos, int hand, List<ComboGroup> groups, CalculationWorkPolicy? policy = null, DrawActivationPriority? priority = null) {
        WorkBudget oldBudget = new(policy ?? CalculationWorkPolicy.Default);
        DrawEffectCalculation old = new(cards, hand, oldBudget, priority);
        ExactCalculationResult oldRows = old.Calculate(combos, groups);
        ProbabilityCalculationResult expected = ProbabilityCalculatorService.CompleteResult(combos, groups, CalculationModel.Fast, oldRows, oldBudget, old);
        WorkBudget budget = new(policy ?? CalculationWorkPolicy.Default);
        DrawEffectCalculation adaptive = new(cards, hand, budget, priority, adaptiveFast: true);
        ExactCalculationResult rows = adaptive.Calculate(combos, groups);
        ProbabilityCalculationResult actual = ProbabilityCalculatorService.CompleteResult(combos, groups, CalculationModel.Fast, rows, budget, adaptive);
        Assert.That(JsonSerializer.Serialize(actual), Is.EqualTo(JsonSerializer.Serialize(expected)));
        Assert.That((budget.Spent, budget.PeakEntries, budget.PeakCells), Is.EqualTo((oldBudget.Spent, oldBudget.PeakEntries, oldBudget.PeakCells)));
    }
}
