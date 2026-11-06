using System.Numerics;
using CardProbability.EngineTests.Fixtures;
using CardProbability.Internal;
using CardProbability.Internal.Certification;
using CardProbability.Internal.Counting;
using CardProbability.Internal.DrawResolution;
using CardProbability.Internal.Model;
using CardProbability.Internal.Resources;
using CardProbability.Internal.Search;
using NUnit.Framework;
using Core = CardProbability.Internal.Contracts;
using NativePriority = CardProbability.Internal.Model.DrawActivationPriority;

namespace CardProbability.EngineTests.Optimizations;

[TestFixture, NonParallelizable]
internal sealed class AdaptiveAndMultipleCopyRegressionTest {
    [Test]
    public void AdaptiveRoutesRetainEveryPhysicalRowOrTheHistoricalCounters() {
        int selected = 0;
        foreach (EngineCase fixture in AdaptiveFixtures()) {
            PreparedCalculation input = fixture.Prepare();
            NativePriority priority = DeferredTestCases.Priority(fixture);
            WorkBudget historicalBudget = new(Core.CalculationWorkPolicy.Default);
            DrawEffectCalculation historical = new(input.Cards, fixture.Request.HandSize, historicalBudget, priority);
            ExactCalculationResult historicalRows = historical.Calculate(input.Combos, input.Groups);
            historical.DescribeCapability();
            WorkBudget budget = new(Core.CalculationWorkPolicy.Default);
            DrawEffectCalculation adaptive = new(input.Cards, fixture.Request.HandSize, budget, priority, adaptiveFast: true);
            ExactCalculationResult actual = adaptive.Calculate(input.Combos, input.Groups);
            CalculationCapability capability = adaptive.DescribeCapability();
            if (capability.Route.ToString().StartsWith("SearchPhysical", StringComparison.Ordinal)) {
                selected++;
                SearchCertificateOracle.Counts oracle = SearchCertificateOracle.Enumerate(fixture);
                AssertRows(actual, oracle.Wins[1], oracle.Outcomes, fixture.Name);
                Assert.That(capability.Accuracy, Is.EqualTo(PhysicalSearchAccuracy.ProvenExact));
            }
            else {
                Assert.That(actual.ToRows().Select(row => row.ToFraction()), Is.EqualTo(historicalRows.ToRows().Select(row => row.ToFraction())), fixture.Name);
                Assert.That((budget.Spent, budget.PeakEntries, budget.PeakCells),
                    Is.EqualTo((historicalBudget.Spent, historicalBudget.PeakEntries, historicalBudget.PeakCells)), fixture.Name);
            }
        }
        Assert.That(selected, Is.GreaterThanOrEqualTo(20));
    }

    [TestCase("searchable-source-no-removal", true)]
    [TestCase("full-depletion", true)]
    [TestCase("selective-failure", true)]
    [TestCase("hidden-source-copies", true)]
    [TestCase("saved-limit-one", true)]
    [TestCase("only-source-copies", true)]
    [TestCase("repeatable-copies", false)]
    [TestCase("saved-limit-two", false)]
    [TestCase("short-deck", false)]
    public void SourceCopyQuotientPreservesFailureMassAndBothSearchBounds(string boundary, bool aggregated) {
        EngineCase fixture = MultipleCopies(boundary);
        PreparedCalculation input = fixture.Prepare();
        SearchCertificateOracle.Counts truth = SearchCertificateOracle.Enumerate(fixture);
        SearchCountingDiagnostics diagnostics = new();
        WorkBudget exactBudget = new(Core.CalculationWorkPolicy.Unlimited) { SearchDiagnostics = diagnostics };
        BigInteger completed = BigInteger.Zero;
        PostDrawCalculation exact = new(input.Cards, fixture.Request.HandSize, exactBudget, deferIdentities: false) {
            ObservePhysicalOutcome = (_, _, _, mass) => completed += mass
        };
        ExactCalculationResult physical = exact.CalculateSearch(input.Combos, input.Groups, false, new(input.Cards, exactBudget),
            physical: true, method: PhysicalEnumerationMethod.SingleSource, coalesceOrdinary: false);
        AssertRows(physical, truth.Wins[1], truth.Outcomes, boundary);
        Assert.That(completed + truth.Failed, Is.EqualTo(truth.Outcomes));
        Assert.That(diagnostics.Counts.GetValueOrDefault("primary/physical single-source frames") > 0, Is.EqualTo(aggregated));
        if (boundary == "selective-failure") {
            Assert.That(truth.Failed, Is.GreaterThan(BigInteger.Zero));
        }
        ExactCalculationResult[][] rows = new ExactCalculationResult[2][];
        foreach (int streamed in new[] { 0, 1 }) {
            WorkBudget budget = new(Core.CalculationWorkPolicy.Unlimited);
            PostDrawCalculation calculation = new(input.Cards, fixture.Request.HandSize, budget, deferIdentities: false) {
                StreamSearchTerminals = streamed == 1
            };
            rows[streamed] = [calculation.CalculateSearch(input.Combos, input.Groups, false, new(input.Cards, budget)),
                calculation.CalculateSearchLower(input.Combos, input.Groups, true),
                calculation.CalculateSearchLower(input.Combos, input.Groups, false)];
        }
        for (int pass = 0; pass < 3; pass++) {
            Assert.That(rows[1][pass].ToRows().Select(row => row.ToFraction()), Is.EqualTo(rows[0][pass].ToRows().Select(row => row.ToFraction())));
        }
        for (int row = 0; row < physical.ToRows().Length; row++) {
            ExactProbability upper = rows[1][0].ToRows()[row];
            ExactProbability witness = rows[1][1].ToRows()[row];
            ExactProbability lower = rows[1][2].ToRows()[row];
            Assert.That(upper.Numerator * truth.Outcomes, Is.GreaterThanOrEqualTo(truth.Wins[1][row] * upper.Denominator));
            Assert.That(witness.Numerator * truth.Outcomes, Is.EqualTo(truth.Wins[4][row] * witness.Denominator));
            Assert.That(lower.Numerator * truth.Outcomes, Is.LessThanOrEqualTo(truth.Wins[1][row] * lower.Denominator));
        }
    }

    private static IEnumerable<EngineCase> AdaptiveFixtures() {
        foreach (EngineCase fixture in SearchCertificateFixtures.Fixtures()) {
            yield return fixture;
        }
        // The certification corpus deliberately has two search sources and
        // eight copies, outside the adaptive physical route's measured domain.
        // Retain that corpus as the historical-counter control, then cover its
        // one-source, reachable-target-loss variants inside the candidate domain.
        foreach (EngineCase fixture in SearchCertificateFixtures.OrderedCoverage()) {
            DeckEntry[] entries = fixture.Request.Entries.Where(entry => entry.Id != "z").Select(entry => {
                if (entry.Id == "s") {
                    return entry with { Copies = 1 };
                }
                if (entry.Id != "d") {
                    return entry;
                }
                EffectPlan effect = entry.Effect!;
                return entry with { Effect = effect with {
                    HandCostEligibility = effect.HandCost > 0 ? new("a") : null,
                    RemovalEligibility = effect.RemovalCount > 0 ? new("a") : null
                } };
            }).ToArray();
            yield return fixture with { Name = "adaptive-" + fixture.Name,
                Request = new(entries, fixture.Request.HandSize, fixture.Request.Goals, fixture.Request.Memberships, fixture.Request.Groups) };
        }
    }
    private static EngineCase MultipleCopies(string boundary) {
        bool one = boundary == "saved-limit-one";
        bool repeatable = boundary is "repeatable-copies" or "saved-limit-two";
        int copies = one || boundary == "only-source-copies" ? 3 : 2;
        int hand = boundary switch { "only-source-copies" => 1, "full-depletion" => 4, "short-deck" => 5, _ => 2 };
        DeckEntry source = new("draw", copies, new(DrawCount: 2,
            ActivationLimit: one ? 1 : boundary == "saved-limit-two" ? 2 : repeatable ? null : 1,
            HiddenDeckCost: boundary is "hidden-source-copies" or "short-deck" ? 1 : 0,
            HiddenPostDrawMill: boundary is "hidden-source-copies" or "full-depletion" or "short-deck" ? 1 : 0,
            RemovalCount: boundary == "selective-failure" ? 2 : boundary == "hidden-source-copies" ? 1 : 0,
            RemovalEligibility: boundary == "selective-failure" ? new("second") : null));
        DeckEntry[] entries = boundary == "only-source-copies" ? [source] : [source,
            new("search", 1, new(Search: new(Eligibility.Any))), new("first", 1), new("second", 1), new("both", 1), new("inert", 1)];
        Goal[] goals = [
            new("none", [Requirement.OfEntry("draw", 0, 0)], groupId: "source"),
            new("one", [Requirement.OfEntry("draw", 1, 1)], groupId: "source"),
            new("two", [Requirement.OfEntry("draw", 2, 2)], groupId: "source"),
            new("roles", [Requirement.InMembership("first", 1, 2), Requirement.InMembership("second", 1, 2)], groupId: "roles"),
            new("exclude", [Requirement.InMembership("second", 0, 0)], groupId: "roles"),
            new("fetched", [Requirement.InMembership("search-entry", 1, 1), Requirement.InMembership("both-entry", 1, 1)])
        ];
        Membership[] memberships = [new("first", entries.Where(entry => entry.Id is "draw" or "first" or "both").Select(entry => entry.Id)),
            new("second", entries.Where(entry => entry.Id is "second" or "both").Select(entry => entry.Id)),
            new("search-entry", entries.Where(entry => entry.Id == "search").Select(entry => entry.Id)),
            new("both-entry", entries.Where(entry => entry.Id == "both").Select(entry => entry.Id))];
        return new(boundary, new(entries, hand, goals, memberships, ["source", "roles", "empty"]), new());
    }

    private static void AssertRows(ExactCalculationResult actual, BigInteger[] wins, BigInteger outcomes, string name) {
        ExactProbability[] rows = actual.ToRows();
        Assert.That(rows, Has.Length.EqualTo(wins.Length));
        for (int row = 0; row < rows.Length; row++) {
            Assert.That(rows[row].Numerator * outcomes, Is.EqualTo(wins[row] * rows[row].Denominator), name + "/" + row);
        }
    }
}
