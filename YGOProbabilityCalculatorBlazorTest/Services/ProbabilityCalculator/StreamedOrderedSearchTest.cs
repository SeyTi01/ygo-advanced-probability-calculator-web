using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture, NonParallelizable]
public sealed class StreamedOrderedSearchTest {
    [TestCase("coalesced-uneven-targets")]
    [TestCase("minimal-filtered-target-discard")]
    [TestCase("mixed-visible-hidden")]
    [TestCase("maximum-only-removal")]
    [TestCase("or-and-filtered-removal")]
    [TestCase("draw-target-source-consumed")]
    [TestCase("multiple-draw-opt-duplicates")]
    [TestCase("solar-style-cost-and-mill")]
    [TestCase("grouped-level-hidden-True-target-source-True")]
    public void ProjectedBoundsContainIndependentPhysicalTruth(string name) {
        SearchCertificateFixtures.Fixture fixture = SearchCertificateFixtures.Fixtures().Single(value => value.Name == name);
        SearchCertificateOracle.Counts truth = SearchCertificateOracle.Enumerate(fixture.Deck, fixture.Hand,
            fixture.Combos, fixture.Groups, fixture.Priority);
        AssertPublicRows(fixture, truth);
    }

    [TestCase(1, "unrestricted")]
    [TestCase(2, "opt")]
    [TestCase(3, "limit")]
    [TestCase(2, "first")]
    [TestCase(3, "lock")]
    public void MultipleSourceCopiesPreserveAllRowsAndCertificatePartitions(int copies, string allowance) {
        CategoryBase first = new("First");
        CategoryBase second = new("Second");
        List<Card> cards = [new([first], copies, id: "draw", drawCount: 2, drawOncePerTurn: allowance == "opt",
                postDrawDiscardCount: 1, postDrawDeckMillCount: allowance == "limit" ? 0 : 1, firstActivationOnly: allowance == "first",
                blocksLaterDraws: allowance == "lock", activationLimit: allowance == "limit" ? 1 : null),
            new([], id: "search", search: new(CardSelector.Any, false)),
            new([first, second], 2, id: "target"), new([], 4 - copies, id: "filler")];
        List<Combo> combos = [new([new(first, 1, 3), new(second, 1, 3)], "Distinct", groupId: "g"),
            new([ComboCategory.SameCard([first, second], 1, 3)], "Same card", groupId: "g"),
            new([new(first, 0, 0)], "Exclude first", groupId: "other"),
            new([], "Unused or fetched draw source", cards: [new("draw", 1, 2)], groupId: "other"),
            new([], "Exactly two sources", cards: [new("draw", 2, 2)], groupId: "other"),
            new([], "Completed")];
        SearchCertificateFixtures.Fixture fixture = new("multiple-" + copies + "-" + allowance, cards, 2, combos,
            [new("g", "Targets"), new("other", "Other"), new("empty", "Empty")]);
        // Enumerate every labeled permutation independently of the sampler,
        // compiler and decision diagram, including unused and fetched sources.
        // The independent scheduler expresses a one-activation allowance as OPT.
        // For this single source with no hand cost, explicit limit one has the
        // identical availability, source consumption and later-copy behavior.
        List<Card> reference = allowance == "limit"
            ? cards.Select(card => card.Id == "draw" ? card.WithOncePerTurn(true) : card).ToList() : cards;
        SearchCertificateOracle.Counts truth = SearchCertificateOracle.Enumerate(reference, fixture.Hand, combos,
            fixture.Groups, collapseIdenticalCopies: false);
        AssertPublicRows(fixture, truth);
    }

    [Test]
    public void ObjectiveNumericMembershipDistinguishesManualOverrides() {
        CategoryBase monster = new("Monster", CategorySource.Metadata, "kind:monster");
        CategoryBase level = new("Level 8", CategorySource.Metadata, "level:8");
        List<Card> cards = [new([], id: "draw", drawCount: 2, postDrawDiscardCount: 1),
            new([], id: "search", search: new(CardSelector.Any, false)),
            new([monster, level], 2, id: "objective", externalCardId: 1),
            new([monster, level], 1, id: "manual", externalCardId: 2, manualMetadataCategoryKeys: ["level:8"]),
            new([], 2, id: "filler")];
        SearchCertificateFixtures.Fixture fixture = new("numeric guard", cards, 3,
            [new([new(new NumericPropertyRange(NumericPropertyKind.Level, 5, null, true).ToCategory(), 1, 3)], "Objective level")], []);
        SearchCertificateOracle.Counts truth = SearchCertificateOracle.Enumerate(cards, 3, fixture.Combos, []);
        AssertPublicRows(fixture, truth);
    }

    private static void AssertPublicRows(SearchCertificateFixtures.Fixture fixture, SearchCertificateOracle.Counts truth) {
        DrawActivationPriority? priority = fixture.Priority is null ? null : new(DrawActivationMode.CustomPriority, fixture.Priority);
        ProbabilityCalculatorService service = new();
        ProbabilityCalculationResult exact = service.CalculateProbabilityResults(
            fixture.Deck, fixture.Combos, fixture.Hand, fixture.Groups, CalculationWorkPolicy.Interactive,
            options: new(priority, Mode: CalculationModel.Exact));
        PhysicalSearchTest.AssertRows(exact, truth.Wins[1], truth.Outcomes);
        ProbabilityCalculationResult projection = service.CalculateProbabilityResults(
            fixture.Deck, fixture.Combos, fixture.Hand, fixture.Groups, CalculationWorkPolicy.Interactive,
            options: new(priority, CertifySearchAvailability: true));
        ApplicationResultFractions primary = ApplicationResultFractions.From(projection);
        ApplicationFraction[] upper = [primary.Total, .. primary.Combos, .. primary.Groups];
        SearchAvailabilityCertificate certificate = projection.SearchAvailability!;
        Assert.That(certificate.Total, Is.Not.Null);
        SearchAvailabilityInterval[] intervals = [certificate.Total!, .. certificate.Combos, .. certificate.Groups];
        Assert.That(intervals, Has.Length.EqualTo(upper.Length));
        for (int row = 0; row < upper.Length; row++) {
            ApplicationFraction lower = ApplicationFraction.From(intervals[row].Lower);
            ApplicationFraction certifiedUpper = ApplicationFraction.From(intervals[row].Upper);
            Assert.That(lower.Numerator * truth.Outcomes,
                Is.LessThanOrEqualTo(truth.Wins[1][row] * lower.Denominator), fixture.Name + "/lower/" + row);
            Assert.That(certifiedUpper.Numerator * truth.Outcomes,
                Is.GreaterThanOrEqualTo(truth.Wins[1][row] * certifiedUpper.Denominator), fixture.Name + "/upper/" + row);
            Assert.That(upper[row].Numerator * truth.Outcomes,
                Is.GreaterThanOrEqualTo(truth.Wins[1][row] * upper[row].Denominator), fixture.Name + "/primary/" + row);
        }
    }
}
