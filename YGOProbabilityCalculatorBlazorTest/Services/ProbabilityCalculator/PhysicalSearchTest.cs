using System.Numerics;
using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture, NonParallelizable]
public sealed class PhysicalSearchTest {
    internal static void AssertRows(ProbabilityCalculationResult result, BigInteger[] wins, BigInteger denominator) {
        ProbabilityFraction[] rows = [result.Contract!.Fractions!.Total, .. result.Contract.Fractions.Combos, .. result.Contract.Fractions.Groups];
        for (int row = 0; row < rows.Length; row++) {
            Assert.That(BigInteger.Parse(rows[row].Numerator) * denominator,
                Is.EqualTo(wins[row] * BigInteger.Parse(rows[row].Denominator)), $"row {row}");
        }
    }

    [Test]
    public void ExhaustiveExistingFixturesAllAgree() {
        int supportedLoss = 0;
        foreach (SearchCertificateFixtures.Fixture fixture in SearchCertificateFixtures.Fixtures()) {
            SearchCertificateOracle.Counts oracle = SearchCertificateOracle.Enumerate(fixture.Deck, fixture.Hand, fixture.Combos, fixture.Groups, fixture.Priority);
            CalculationOptions options = new(fixture.Priority is null ? null : new(DrawActivationMode.CustomPriority, fixture.Priority), Mode: CalculationModel.Exact);
            ProbabilityCalculationResult result = new ProbabilityCalculatorService().CalculateProbabilityResults(
                fixture.Deck, fixture.Combos, fixture.Hand, fixture.Groups, CalculationWorkPolicy.Default, options);
            AssertRows(result, oracle.Wins[1], oracle.Outcomes);
            Assert.That(result.Contract!.Accuracy, Is.EqualTo(CalculationAccuracy.Exact), fixture.Name);
            if (result.Contract.SolverRoute == "SearchPhysicalCountState") {
                supportedLoss++;
            }
        }
        Assert.That(supportedLoss, Is.GreaterThan(10));
    }

    [TestCase(1, false, false), TestCase(1, true, true)]
    [TestCase(2, false, false), TestCase(2, false, true), TestCase(2, true, false), TestCase(2, true, true)]
    public void ConsumedCopiesRemainUnavailableAcrossOptAndIdChanges(int copies, bool opt, bool rename) {
        CategoryBase target = new("Target");
        List<Card> deck = [new([], 1, "same", id: rename ? "zz" : "s", search: new(CardSelector.For(target), false)),
            new([target], copies, "same", id: rename ? "aa" : "d", drawCount: 1, drawOncePerTurn: opt), new([], 1, id: "f")];
        List<Combo> combos = [new([new(target, 1, 3)], "Target"), new([new(target, 0, 0)], "Excluded")];
        SearchCertificateOracle.Counts oracle = SearchCertificateOracle.Enumerate(deck, 1, combos, []);
        ProbabilityCalculationResult result = new ProbabilityCalculatorService().CalculateProbabilityResults(deck, combos, 1, null,
            CalculationWorkPolicy.Default, new(Mode: CalculationModel.Exact));
        AssertRows(result, oracle.Wins[1], oracle.Outcomes);
        if (copies == 1) {
            Assert.That(result.Contract!.Fractions!.Combos[0], Is.EqualTo(new ProbabilityFraction("1", "3")));
        }
    }

    // Independent physical draw scheduler + search/slot oracle: every labeled
    // permutation is counted; no production scenario, compiler or diagram.
    [TestCase(false, false), TestCase(false, true), TestCase(true, false), TestCase(true, true)]
    public void JointDepletionWithConsumedSourcesAndIndependentEntryLimits(bool opt, bool split) {
        CategoryBase a = new("A");
        CategoryBase b = new("B");
        List<Card> deck = [
            new([b], 1, "same", id: "s", search: new(CardSelector.Any, false), oncePerTurn: opt),
            new([a], 1, "same", id: "z", search: new(CardSelector.For(a), false), oncePerTurn: opt),
            new([a, b], 2, id: "t"),
            new([a], split ? 1 : 2, "draw", id: "d", drawCount: 1, drawOncePerTurn: opt, preDrawDeckRemovalCount: 1),
            new([b], 1, "draw", id: "e", drawCount: 1, drawOncePerTurn: opt),
            new([], 1, id: "f")
        ];
        if (split) {
            deck.Add(new([a], 1, "draw", id: "other-d", drawCount: 1, drawOncePerTurn: opt, postDrawDeckMillCount: 1));
        }
        List<ComboGroup> groups = [new("g", "Distinct and exclusion"), new("h", "Direct and alternative")];
        List<Combo> combos = [
            new([new(a, 1, 3), new(b, 1, 3)], "Distinct A+B", groupId: "g"),
            new([new(a, 0, 0)], "No A", groupId: "g"),
            new([new(b, 0, 1)], "At most one B", groupId: "g"),
            new([], "Search source forbidden", groupId: "h", cards: [new("s", 0, 0), new("d", 1, 3)]),
            new([ComboCategory.SameCard([a, b], 1, 1)], "One same-card", groupId: "h"),
            new([], "OR", groupId: "h", alternativeGroups: [new([
                ComboAlternative.For(new ComboCard("d", 1, 3)), ComboAlternative.For(new ComboCategory(b, 2, 3))])])
        ];
        string[] priority = ["e", "other-d", "d"];
        SearchCertificateOracle.Counts oracle = SearchCertificateOracle.Enumerate(deck, 2, combos, groups, priority);
        Assert.That(oracle.Outcomes, Is.EqualTo(new BigInteger(40320)));
        Assert.That(oracle.Failed, Is.EqualTo(BigInteger.Zero));
        TestContext.Out.WriteLine($"joint opt={opt} split={split}: physical rows [{string.Join(',', oracle.Wins[1])}] / {oracle.Outcomes}");
        ProbabilityCalculationResult exact = new ProbabilityCalculatorService().CalculateProbabilityResults(deck, combos, 2, groups,
            CalculationWorkPolicy.Default, new(new(DrawActivationMode.CustomPriority, priority), Mode: CalculationModel.Exact));
        AssertRows(exact, oracle.Wins[1], oracle.Outcomes);
        Assert.That(exact.Contract!.SolverRoute, Is.EqualTo("SearchPhysicalCountState"));
    }

    [TestCase(false), TestCase(true)]
    public void RedundantSingleOptRestrictionsAndActualLostTargetWitness(bool mill) {
        List<Card> deck = ThreeCalculationModesTest.Counterexample(mill ? "mill" : "banish");
        Card previous = deck[^1];
        deck[^1] = new([], 1, id: "d", drawCount: 1, drawOncePerTurn: true, firstActivationOnly: true,
            blocksLaterDraws: true, preDrawDeckRemovalCount: previous.PreDrawDeckRemovalCount,
            postDrawDeckMillCount: previous.PostDrawDeckMillCount);
        List<Combo> combos = [ThreeCalculationModesTest.Combos[0]];
        SearchCertificateOracle.Counts oracle = SearchCertificateOracle.Enumerate(deck, 2, combos, []);
        Assert.That(oracle.Outcomes, Is.EqualTo(new BigInteger(120)));
        Assert.That(oracle.Wins[1][0], Is.EqualTo(new BigInteger(92)));
        Card[] order = mill ? [deck[0], deck[3], deck[2], deck[1], deck[2]] : [deck[0], deck[3], deck[1], deck[2], deck[2]];
        IReadOnlyList<Card> rest = [];
        List<Card> hand = DrawSequenceOracle.Resolve(order, 2, combos, remainingDeck: cards => rest = cards)!;
        Assert.That(rest.Any(card => card.Id == "a"), Is.False);
        Assert.That(SearchCertificateOracle.Search(deck, hand, deck.Select(card => rest.Count(value => value.Id == card.Id)).ToArray(), rest.Count, combos, []).Results[0], Is.False);
        ProbabilityCalculationResult exact = new ProbabilityCalculatorService().CalculateProbabilityResults(deck, combos, 2, null,
            CalculationWorkPolicy.Default, new(Mode: CalculationModel.Exact));
        AssertRows(exact, oracle.Wins[1], oracle.Outcomes);
    }

    [TestCase(false), TestCase(true)]
    public void FortyCardHiddenLossFitsDefaultStandardAndUnchangedFastBounds(bool banish) {
        SessionState session = SearchAvailabilityCertificateTest.Example(banish ? "banish10" : "mill2").GetAwaiter().GetResult();
        Assert.That(session.Cards.Sum(card => card.Copies), Is.EqualTo(40));
        Assert.That(session.Cards.Count(card => card.Search is not null), Is.EqualTo(3));
        ProbabilityCalculatorService service = new();
        ProbabilityCalculationResult fast = service.CalculateProbabilityResults(session.Cards, session.Combos, session.HandSize,
            session.ComboGroups, CalculationWorkPolicy.Default, new(Mode: CalculationModel.Fast));
        ProbabilityCalculationResult exact = service.CalculateProbabilityResults(session.Cards, session.Combos, session.HandSize,
            session.ComboGroups, CalculationWorkPolicy.Default, new(Mode: CalculationModel.Exact));
        Assert.That(exact.Contract!.SolverRoute, Is.EqualTo("SearchPhysicalCountState"));
        Assert.That(exact.SearchAvailability, Is.Null);
        ProbabilityFraction[] values = [exact.Contract.Fractions!.Total, .. exact.Contract.Fractions.Combos, .. exact.Contract.Fractions.Groups];
        SearchAvailabilityInterval[] bounds = [fast.SearchAvailability!.Total!, .. fast.SearchAvailability.Combos, .. fast.SearchAvailability.Groups];
        for (int row = 0; row < values.Length; row++) {
            Assert.That(Compare(bounds[row].Lower, values[row]), Is.LessThanOrEqualTo(0));
            Assert.That(Compare(values[row], bounds[row].Upper), Is.LessThanOrEqualTo(0));
        }
        Assert.That(Compare(exact.Contract.Fractions.Total, fast.Contract!.Fractions!.Total), Is.LessThan(0));
    }

    [TestCase(false), TestCase(true)]
    public void PrimaryLimitsNeverPublishPartialPhysicalRows(bool storage) {
        List<Card> deck = ThreeCalculationModesTest.Counterexample("banish");
        List<ComboGroup> groups = storage ? Enumerable.Range(0, 32768).Select(index => new ComboGroup("g" + index, "Group")).ToList() : [];
        WorkBudget complete = new(CalculationWorkPolicy.Default);
        new DrawEffectCalculation(deck, 2, complete, physicalSearch: true).Calculate(ThreeCalculationModesTest.Combos, []);
        CalculationSnapshot snapshot = CalculationSnapshot.Capture(deck, ThreeCalculationModesTest.Combos, 2, groups,
            storage ? CalculationWorkPolicy.Default : new(complete.Spent / 2), options: new(Mode: CalculationModel.Exact));
        CalculationResponse response = JsonSerializer.Deserialize(CalculationWire.Execute(snapshot.Json), CalculationJsonContext.Default.CalculationResponse)!;
        Assert.That(response.Result, Is.Null);
        Assert.That(response.Contract!.Fractions, Is.Null);
        Assert.That(response.Contract.Completion, Is.EqualTo(storage ? CalculationCompletion.StorageLimit : CalculationCompletion.WorkLimit));
        Assert.That(response.LimitReason, Is.EqualTo(storage ? ProbabilityCalculationLimitReason.Storage : ProbabilityCalculationLimitReason.Work));
    }

    private static int Compare(ProbabilityFraction left, ProbabilityFraction right) =>
        (BigInteger.Parse(left.Numerator) * BigInteger.Parse(right.Denominator)).CompareTo(BigInteger.Parse(right.Numerator) * BigInteger.Parse(left.Denominator));
}
