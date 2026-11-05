using System.Numerics;
using CardProbability.EngineTests.Fixtures;
using CardProbability.Internal;
using CardProbability.Internal.Certification;
using CardProbability.Internal.Counting;
using CardProbability.Internal.DrawResolution;
using CardProbability.Internal.Resources;
using Core = CardProbability.Internal.Contracts;
using static CardProbability.EngineTests.Optimizations.DeferredTestCases;

namespace CardProbability.EngineTests.Optimizations;

[TestFixture, NonParallelizable]
public sealed class SelectiveSurvivorTest {
    [TestCase(0, false), TestCase(1, false), TestCase(2, false), TestCase(3, false)]
    [TestCase(1, true), TestCase(2, true), TestCase(3, true)]
    public void SelectivePaymentsAgreeWithChronologicalPhysicalOracle(int payments, bool splitAndPrioritize) {
        bool split = splitAndPrioritize && payments > 1;
        List<DeckEntry> entries = [Draw("cost-draw", 2, 1, cost: 1, costSet: "material"),
            Draw("hidden-draw", count: 1, limit: 1, hidden: 1), new("search", 1, new(Search: new(new("target")), ActivationLimit: null)),
            new("ordinary", 1), new("filler", 3 - payments), new("payment", split ? payments - 1 : payments)];
        if (split) {
            entries.Add(new("second-payment", 1));
        }
        EngineCase fixture = Create("selective payment", entries, 2,
            [new("distinct", [Member("target", 1, 3), Member("material", 1, 3)], groupId: "g"),
                new("intersection", [Member("both", 1, 1)], groupId: "g"), new("exclude", [Member("target", 0, 0)], groupId: "h"),
                new("direct-exclude", [Entry("cost-draw", 0, 0), Entry("search", 0, 0)], groupId: "h"),
                new("alternative", [], [new([Entry("payment", 1, 2), Member("target", 2, 3)])], groupId: "g")],
            [new("material", split ? ["payment", "second-payment"] : ["payment"]),
                new("target", ["cost-draw", "ordinary", "payment"]), new("both", ["payment"])], ["empty", "g", "h"],
            splitAndPrioritize ? ["hidden-draw", "cost-draw"] : null);
        AssertPhysical(fixture, SolverRoute.SearchPhysicalDeferred, compareStreaming: true);
    }

    [TestCase(2, false), TestCase(3, true), TestCase(6, true)]
    public void SharedCostsRemovableSearchersAndFailedPostDrawRequirements(int hand, bool postRemoval) {
        EngineCase fixture = Create("shared removable costs", [Draw("first", count: 1, limit: 1, cost: 1, costSet: "material", mill: 1),
            Draw("second", count: 1, limit: 1, cost: postRemoval ? 0 : 1, costSet: postRemoval ? null : "material",
                removal: postRemoval ? 1 : 0, removalSet: postRemoval ? "material" : null),
            Draw("hidden", count: 1, limit: 1, hidden: 1, mill: 1), new("payment", 1),
            new("search", 1, new(Search: new(new("target")), ActivationLimit: null)),
            new("other-search", 1, new(Search: new(new("target")), ActivationLimit: null)), new("target", 1), new("filler", 1)], hand,
            [Goal("target", Member("target", 1, 1)), Goal("exclusion", Member("material", 0, 0)),
                Goal("direct", Entry("first", 1, 8), Entry("search", 0, 0))],
            [new("material", postRemoval ? ["payment", "search"] : ["payment"]), new("target", ["first", "payment", "target"])],
            priority: ["second", "hidden", "first"]);
        AssertPhysical(fixture, SolverRoute.SearchPhysicalDeferred);
    }

    [TestCase(false), TestCase(true)]
    public void UnsupportedPoolObservationsRetainPhysicalFallback(bool activationRestriction) {
        EffectPlan effect = new(DrawCount: 1, HandCost: 1, HandCostEligibility: activationRestriction ? new("target") : null,
            FirstActivationOnly: activationRestriction, HiddenPostDrawMill: 1);
        EngineCase fixture = Create("physical fallback", [new("target", 1), new("filler", 2),
            new("search", 1, new(Search: new(new("target")), ActivationLimit: null)), new("draw", 1, effect)], 2,
            [Goal("target", Member("target", 1, 2))], [new("target", ["target"])]);
        AssertPhysical(fixture, SolverRoute.SearchPhysicalFrontier);
    }

    private static void AssertPhysical(EngineCase fixture, SolverRoute route, bool compareStreaming = false) {
        SearchCertificateOracle.Counts oracle = SearchCertificateOracle.Enumerate(fixture);
        WorkBudget budget = new(Core.CalculationWorkPolicy.Interactive);
        PreparedCalculation input = fixture.Prepare(budget);
        BigInteger mass = 0;
        PostDrawCalculation execution = new(input.Cards, fixture.Request.HandSize, budget, Priority(fixture)) {
            ObserveTerminal = (_, weight) => mass += weight
        };
        ExactCalculationResult actual = execution.CalculateSearch(input.Combos, input.Groups, false, new(input.Cards, budget), physical: true);
        Assert.That(execution.Route, Is.EqualTo(route));
        Assert.That(mass + oracle.Failed, Is.EqualTo(oracle.Outcomes));
        ExactProbability[] rows = actual.ToRows();
        for (int row = 0; row < rows.Length; row++) {
            Assert.That(rows[row].Numerator * oracle.Outcomes, Is.EqualTo(oracle.Wins[1][row] * rows[row].Denominator), "oracle row " + row);
        }
        if (compareStreaming) {
            WorkBudget referenceBudget = new(Core.CalculationWorkPolicy.Interactive);
            PostDrawCalculation reference = new(input.Cards, fixture.Request.HandSize, referenceBudget, Priority(fixture));
            ExactCalculationResult expected = reference.CalculateSearch(input.Combos, input.Groups, false, new(input.Cards, referenceBudget),
                physical: true, method: PhysicalEnumerationMethod.Streaming);
            Assert.That(rows.Select(value => value.ToFraction()), Is.EqualTo(expected.ToRows().Select(value => value.ToFraction())));
        }
    }
}
