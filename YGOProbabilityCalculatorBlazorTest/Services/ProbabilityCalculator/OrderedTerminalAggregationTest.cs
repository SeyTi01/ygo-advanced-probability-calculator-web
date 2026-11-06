using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture, NonParallelizable]
public sealed class OrderedTerminalAggregationTest {
    [Test]
    public void PublishedPrimaryAndCertificateRowsMatchPhysicalOracle() {
        foreach (SearchCertificateFixtures.Fixture fixture in SearchCertificateFixtures.Fixtures()) {
            List<Card> deck = fixture.Deck.Where(card => card.Active && card.Copies > 0).ToList();
            DrawActivationPriority? priority = fixture.Priority is null ? null : new(DrawActivationMode.CustomPriority, fixture.Priority);
            ProbabilityCalculationResult result = new ProbabilityCalculatorService().CalculateProbabilityResults(
                deck, fixture.Combos, fixture.Hand, fixture.Groups, CalculationWorkPolicy.Interactive, new(priority, true));
            ApplicationFraction[] primary = Rows(ApplicationResultFractions.From(result));
            SearchAvailabilityCertificate certificate = result.SearchAvailability!;
            SearchAvailabilityInterval[] bounds = [certificate.Total!, .. certificate.Combos, .. certificate.Groups];
            SearchCertificateOracle.Counts truth = SearchCertificateOracle.Enumerate(deck, fixture.Hand, fixture.Combos, fixture.Groups, fixture.Priority);
            for (int row = 0; row < primary.Length; row++) {
                ApplicationFraction lower = ApplicationFraction.From(bounds[row].Lower);
                ApplicationFraction upper = ApplicationFraction.From(bounds[row].Upper);
                Assert.That(primary[row].Numerator * truth.Outcomes, Is.EqualTo(truth.Wins[2][row] * primary[row].Denominator), fixture.Name);
                Assert.That(lower.Numerator * truth.Outcomes, Is.GreaterThanOrEqualTo(truth.Wins[4][row] * lower.Denominator), fixture.Name);
                Assert.That(lower.Numerator * truth.Outcomes, Is.LessThanOrEqualTo(truth.Wins[1][row] * lower.Denominator), fixture.Name);
                Assert.That(upper.Numerator * truth.Outcomes, Is.GreaterThanOrEqualTo(truth.Wins[1][row] * upper.Denominator), fixture.Name);
            }
        }
    }

    [Test]
    public void NoSearchOrderedAndDeferredHandsMatchLabeledOracle() {
        foreach (SearchCertificateFixtures.Fixture fixture in SearchCertificateFixtures.OrderedCoverage()) {
            List<Card> deck = fixture.Deck.Select(card => card.WithSearch(null)).ToList();
            DrawActivationPriority? priority = fixture.Priority is null ? null : new(DrawActivationMode.CustomPriority, fixture.Priority);
            DrawSequenceOracle.Counts truth = DrawSequenceOracle.Enumerate(deck, fixture.Hand, fixture.Combos, fixture.Groups, priorityIds: fixture.Priority);
            foreach (CalculationModel mode in new[] { CalculationModel.Fast, CalculationModel.Exact }) {
                ProbabilityCalculationResult result = new ProbabilityCalculatorService().CalculateProbabilityResults(
                    deck, fixture.Combos, fixture.Hand, fixture.Groups, CalculationWorkPolicy.Interactive, new(priority, Mode: mode));
                ApplicationFraction[] rows = Rows(ApplicationResultFractions.From(result));
                for (int row = 0; row < rows.Length; row++) {
                    Assert.That(rows[row].Numerator * truth.Total, Is.EqualTo(truth.Wins[row] * rows[row].Denominator), fixture.Name);
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
        ProbabilityCalculatorService service = new();
        ProbabilityCalculationResult reference = service.CalculateProbabilityResults(state.Cards, state.Combos, state.HandSize, state.ComboGroups,
            CalculationWorkPolicy.Interactive, CalculationStoragePolicy.High, new(state.DrawActivationPriority, true));
        ProbabilityCalculationResult actual = service.CalculateProbabilityResults(state.Cards, state.Combos, state.HandSize, state.ComboGroups,
            CalculationWorkPolicy.Interactive, CalculationStoragePolicy.Standard, new(state.DrawActivationPriority, true));
        Assert.That(Rows(ApplicationResultFractions.From(actual)), Is.EqualTo(Rows(ApplicationResultFractions.From(reference))));
    }



    private static ApplicationFraction[] Rows(ApplicationResultFractions result) => [result.Total, .. result.Combos, .. result.Groups];
}
