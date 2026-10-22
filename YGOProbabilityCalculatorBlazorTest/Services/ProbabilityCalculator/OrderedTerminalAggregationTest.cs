using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture, NonParallelizable]
public sealed class OrderedTerminalAggregationTest {
    [TestCase(false), TestCase(true)]
    public void CompleteTerminalMassAndEveryCertificateRowMatchHistoricalAndPhysicalOracle(bool defer) {
        foreach (SearchCertificateFixtures.Fixture fixture in SearchCertificateFixtures.Fixtures()) {
            List<Card> deck = fixture.Deck.Where(card => card.Active && card.Copies > 0).ToList();
            DrawActivationPriority? priority = fixture.Priority is null ? null : new(DrawActivationMode.CustomPriority, fixture.Priority);
            Dictionary<string, BigInteger>[] distributions = [new(), new()];
            ExactProbability[][] upper = new ExactProbability[2][];
            ExactProbability[][] witness = new ExactProbability[2][];
            ExactProbability[][] lower = new ExactProbability[2][];
            for (int route = 0; route < 2; route++) {
                int side = route;
                WorkBudget budget = new(CalculationWorkPolicy.Interactive);
                PostDrawCalculation calculation = new(deck, fixture.Hand, budget, priority, defer,
                    compactFrontierThreshold: 0, eagerTerminalAggregation: route == 1) {
                    StreamSearchTerminals = false,
                    ObserveTerminal = (hand, mass) => {
                        distributions[side].Add(string.Join(',', hand), mass);
                        Array.Fill(hand, int.MinValue); // Audit copies cannot corrupt retained keys.
                    }
                };
                upper[route] = Rows(calculation.CalculateSearch(fixture.Combos, fixture.Groups, false,
                    new(deck, budget), coalesceOrdinary: defer));
                witness[route] = Rows(calculation.CalculateSearchLower(fixture.Combos, fixture.Groups, true));
                lower[route] = Rows(calculation.CalculateSearchLower(fixture.Combos, fixture.Groups, false));
                Assert.That(calculation.PackedLayers, Is.GreaterThan(0), fixture.Name);
            }
            Assert.That(distributions[1], Is.EquivalentTo(distributions[0]), fixture.Name + " terminal masses");
            Assert.That(upper[1], Is.EqualTo(upper[0]), fixture.Name + " upper");
            Assert.That(witness[1], Is.EqualTo(witness[0]), fixture.Name + " witness");
            Assert.That(lower[1], Is.EqualTo(lower[0]), fixture.Name + " lower");
            SearchCertificateOracle.Counts truth = SearchCertificateOracle.Enumerate(deck, fixture.Hand,
                fixture.Combos, fixture.Groups, fixture.Priority);
            for (int row = 0; row < upper[1].Length; row++) {
                Assert.That(upper[1][row].Numerator * truth.Outcomes,
                    Is.EqualTo(truth.Wins[2][row] * upper[1][row].Denominator), fixture.Name);
                Assert.That(witness[1][row].Numerator * truth.Outcomes,
                    Is.EqualTo(truth.Wins[4][row] * witness[1][row].Denominator), fixture.Name);
                Assert.That(lower[1][row].Numerator * truth.Outcomes,
                    Is.LessThanOrEqualTo(truth.Wins[1][row] * lower[1][row].Denominator), fixture.Name);
                Assert.That(upper[1][row].Numerator * truth.Outcomes,
                    Is.GreaterThanOrEqualTo(truth.Wins[1][row] * upper[1][row].Denominator), fixture.Name);
            }
        }
    }

    [Test]
    public void NoSearchOrderedAndDeferredHandsMatchLabeledOracle() {
        foreach (SearchCertificateFixtures.Fixture fixture in SearchCertificateFixtures.OrderedCoverage()) {
            List<Card> deck = fixture.Deck.Select(card => card.WithSearch(null)).ToList();
            DrawActivationPriority? priority = fixture.Priority is null ? null : new(DrawActivationMode.CustomPriority, fixture.Priority);
            DrawSequenceOracle.Counts truth = DrawSequenceOracle.Enumerate(deck, fixture.Hand, fixture.Combos, fixture.Groups, priorityIds: fixture.Priority);
            foreach (bool defer in new[] { false, true }) {
                PostDrawCalculation historical = new(deck, fixture.Hand, new(CalculationWorkPolicy.Interactive), priority, defer,
                    compactFrontierThreshold: 0, eagerTerminalAggregation: false);
                PostDrawCalculation candidate = new(deck, fixture.Hand, new(CalculationWorkPolicy.Interactive), priority, defer,
                    compactFrontierThreshold: 0);
                ExactProbability[] actual = Rows(candidate.Calculate(fixture.Combos, fixture.Groups));
                Assert.That(actual, Is.EqualTo(Rows(historical.Calculate(fixture.Combos, fixture.Groups))), fixture.Name);
                for (int row = 0; row < actual.Length; row++) {
                    Assert.That(actual[row].Numerator * truth.Total,
                        Is.EqualTo(truth.Wins[row] * actual[row].Denominator), fixture.Name);
                }
            }
        }
    }

    internal static SessionState Pressure(string variant = "base", int roles = 10) {
        List<CategoryBase> categories = Enumerable.Range(0, roles).Select(i => new CategoryBase($"Role{i}")).ToList();
        List<Card> cards = categories.Select((category, index) => new Card([category], 3, id: $"r{index}")).ToList();
        cards.Add(new([], variant == "copies" ? 3 : 2, id: "d", drawCount: 2, postDrawDiscardCount: 1));
        cards.Add(new([], variant == "copies" ? 1 : 2, id: "e", drawCount: 1, drawOncePerTurn: false, postDrawDiscardCount: 1));
        cards.Add(new([], 1, id: "s", search: new(CardSelector.For(categories[0]), false)));
        cards.Add(new([], 40 - cards.Sum(card => card.Copies), id: "f"));

        return new() {
            Cards = cards, Categories = categories, HandSize = 5,
            Combos = categories.Select((category, index) => new Combo([new(category, 1, 5)], $"Row{index}", groupId: "g")).ToList(),
            ComboGroups = [new("g", "All")],
            DrawActivationPriority = variant == "priority" ? new(DrawActivationMode.CustomPriority, ["e", "d"]) : DrawActivationPriority.Default
        };
    }

    [TestCase("base"), TestCase("priority")]
    public void StandardRescuesHistoricalStorageFailureWithEveryHighReferenceRow(string variant) {
        SessionState state = Pressure(variant);
        WorkBudget oldBudget = new(CalculationWorkPolicy.Interactive);
        PostDrawCalculation old = new(state.Cards, state.HandSize, oldBudget, state.DrawActivationPriority, eagerTerminalAggregation: false);
        Assert.That(Assert.Throws<ProbabilityCalculationLimitException>(() => old.CalculateSearch(state.Combos, state.ComboGroups,
            false, new(state.Cards, oldBudget)))!.Reason, Is.EqualTo(ProbabilityCalculationLimitReason.Storage));
        WorkBudget referenceBudget = new(CalculationWorkPolicy.Interactive, CalculationStoragePolicy.High);
        PostDrawCalculation reference = new(state.Cards, state.HandSize, referenceBudget, state.DrawActivationPriority, eagerTerminalAggregation: false);
        ExactCalculationResult expected = reference.CalculateSearch(state.Combos, state.ComboGroups, false, new(state.Cards, referenceBudget));
        WorkBudget candidateBudget = new(CalculationWorkPolicy.Interactive);
        PostDrawCalculation candidate = new(state.Cards, state.HandSize, candidateBudget, state.DrawActivationPriority);
        ExactCalculationResult actual = candidate.CalculateSearch(state.Combos, state.ComboGroups, false, new(state.Cards, candidateBudget));
        Assert.That(Rows(actual), Is.EqualTo(Rows(expected)));
        Assert.That(candidate.PeakCells, Is.LessThan(240_000));
        Assert.That(candidate.PeakConcurrentCells, Is.LessThan(reference.PeakConcurrentCells));
        Assert.That(candidateBudget.Spent, Is.LessThan(referenceBudget.Spent));
        Assert.That(candidate.TerminalStates, Is.EqualTo(reference.TerminalStates));
    }

    [Test]
    public void UnpackedSmallRequestsKeepEveryResourceCounter() {
        SessionState state = Pressure(roles: 4);
        WorkBudget[] budgets = [new(CalculationWorkPolicy.Interactive), new(CalculationWorkPolicy.Interactive)];
        ExactCalculationResult[] results = new ExactCalculationResult[2];
        for (int i = 0; i < 2; i++) {
            PostDrawCalculation calculation = new(state.Cards, state.HandSize, budgets[i], eagerTerminalAggregation: i == 1);
            results[i] = calculation.CalculateSearch(state.Combos, state.ComboGroups, false, new(state.Cards, budgets[i]));
            Assert.That(calculation.PackedLayers, Is.Zero);
        }
        Assert.That(Rows(results[1]), Is.EqualTo(Rows(results[0])));
        Assert.That(budgets[1].Spent, Is.EqualTo(budgets[0].Spent));
        Assert.That(budgets[1].PeakEntries, Is.EqualTo(budgets[0].PeakEntries));
        Assert.That(budgets[1].PeakCells, Is.EqualTo(budgets[0].PeakCells));
    }

    [Test]
    public void SecondaryWorkLimitRetainsTheCompletedWitnessOnARescuedRequest() {
        SessionState state = Pressure();
        WorkBudget measure = new(CalculationWorkPolicy.Interactive);
        DrawEffectCalculation original = new(state.Cards, state.HandSize, measure);
        ExactCalculationResult upper = original.Calculate(state.Combos, state.ComboGroups);
        original.DescribeCapability();
        ExactCalculationResult witness = original.SearchExecution!.CalculateLower(state.Combos, state.ComboGroups, true);
        WorkBudget limited = new(new(measure.Spent + 10_000));
        DrawEffectCalculation execution = new(state.Cards, state.HandSize, limited);
        ExactCalculationResult actual = execution.Calculate(state.Combos, state.ComboGroups);
        SearchAvailabilityCertificate certificate = SearchAvailabilityCertification.TryCalculate(state.Combos,
            state.ComboGroups, actual, limited, execution);
        Assert.That(Rows(actual), Is.EqualTo(Rows(upper)));
        Assert.That(certificate.Status, Is.EqualTo(SearchAvailabilityStatus.Bounded));
        Assert.That(certificate.Detail, Does.Contain("Limited refinement"));
        SearchAvailabilityInterval[] intervals = [certificate.Total!, .. certificate.Combos, .. certificate.Groups];
        ExactProbability[] expectedLower = Rows(witness);
        ExactProbability[] expectedUpper = Rows(upper);
        for (int row = 0; row < intervals.Length; row++) {
            Assert.That(intervals[row].Lower, Is.EqualTo(expectedLower[row].ToFraction()));
            Assert.That(intervals[row].Upper, Is.EqualTo(expectedUpper[row].ToFraction()));
        }
    }

    private static ExactProbability[] Rows(ExactCalculationResult result) => [result.Total, .. result.Combos, .. result.Groups];
}
