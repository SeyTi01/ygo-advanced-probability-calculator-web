using CardProbability.EngineTests.Fixtures;
using CardProbability.Internal;
using CardProbability.Internal.Certification;
using CardProbability.Internal.Contracts;
using CardProbability.Internal.Counting;
using CardProbability.Internal.Resources;

namespace CardProbability.EngineTests;

[TestFixture]
internal sealed class CapabilityProofTest {
    [TestCase("ordinary", SolverRoute.Ordinary)]
    [TestCase("inert", SolverRoute.Ordinary)]
    [TestCase("count", SolverRoute.CountState)]
    [TestCase("single-lock", SolverRoute.CountState)]
    [TestCase("ordered", SolverRoute.Ordered)]
    [TestCase("exchangeable", SolverRoute.Exchangeable)]
    [TestCase("deferred", SolverRoute.OrderedDeferred)]
    public void NoSearchRoutesAreExactWithoutAdditionalProofWork(string kind, SolverRoute expectedRoute) {
        List<DeckEntry> entries = [new("a", 1), new("b", 1), new("material", 1), new("filler", 2)];
        if (kind != "ordinary") {
            entries.Add(new("draw", 1, new(DrawCount: kind is "inert" or "exchangeable" ? 2 : 1,
                ActivationLimit: kind == "exchangeable" ? null : 1, HiddenDeckCost: kind == "inert" ? 10 : 0,
                RemovalCount: kind is "exchangeable" or "deferred" ? 1 : 0,
                RemovalEligibility: kind == "deferred" ? new("material") : null,
                FirstActivationOnly: kind is "ordered" or "single-lock", BlocksLaterDraws: kind is "ordered" or "single-lock")));
        }
        if (kind == "ordered") {
            entries.Add(new("other-draw", 1, new(DrawCount: 1)));
        }
        EngineCase fixture = new(kind, new(entries, 2, [new("a", [Requirement.InMembership("a")]),
            new("none", [Requirement.InMembership("a", 0, 0)])], [new("a", ["a"]), new("material", ["material"])]), new());
        PreparedCalculation model = fixture.Prepare();
        WorkBudget budget = new(CalculationWorkPolicy.Interactive);
        DrawEffectCalculation execution = new(model.Cards, 2, budget);
        ExactCalculationResult result = execution.Calculate(model.Combos, model.Groups);
        (long work, long entriesUsed, long cells) = (budget.Spent, budget.PeakEntries, budget.PeakCells);
        Assert.That(execution.DescribeCapability(), Is.EqualTo(CalculationCapability.NoSearch(expectedRoute)));
        Assert.That((budget.Spent, budget.PeakEntries, budget.PeakCells), Is.EqualTo((work, entriesUsed, cells)));
        DrawSequenceOracle.Counts truth = DrawSequenceOracle.Enumerate(fixture);
        ExactProbability[] rows = [result.Total, .. result.Combos, .. result.Groups];
        for (int row = 0; row < rows.Length; row++) {
            Assert.That(rows[row].Numerator * truth.Total, Is.EqualTo(truth.Wins[row] * rows[row].Denominator));
        }
    }
    [TestCase("consumed", CapabilityReason.ConsumedSearchTarget, CertificationSupport.ElementaryCountStateLowerPass)]
    [TestCase("discard", CapabilityReason.HandRemovalMayRemoveTarget, CertificationSupport.OrderedTerminalLowerPass)]
    [TestCase("banish", CapabilityReason.HiddenDeckLossMayRemoveTarget, CertificationSupport.ElementaryCountStateLowerPass)]
    [TestCase("mill", CapabilityReason.HiddenDeckLossMayRemoveTarget, CertificationSupport.ElementaryCountStateLowerPass)]
    public void PhysicalTargetLossCannotReceiveAnExactProof(string kind, CapabilityReason reason, CertificationSupport support) {
        List<DeckEntry> entries = [new("search", 1, new(Search: new(new("target"))))];
        if (kind == "consumed") {
            entries.Add(new("draw", 1, new(DrawCount: 1)));
            entries.Add(new("filler", 1));
        }
        else {
            entries.Add(new("target", 1));
            entries.Add(new("filler", 2));
            entries.Add(new("draw", 1, new(DrawCount: 1, HiddenDeckCost: kind == "banish" ? 1 : 0,
                HiddenPostDrawMill: kind == "mill" ? 1 : 0, RemovalCount: kind == "discard" ? 1 : 0,
                RemovalEligibility: kind == "discard" ? new("target") : null)));
        }
        string target = kind == "consumed" ? "draw" : "target";
        int hand = kind == "consumed" ? 1 : 2;
        EngineCase fixture = new(kind, new(entries, hand, [new("goal", [Requirement.InMembership("target")])],
            [new("target", [target])]), new());
        PreparedCalculation model = fixture.Prepare();
        WorkBudget budget = new(CalculationWorkPolicy.Interactive);
        DrawEffectCalculation execution = new(model.Cards, hand, budget);
        Assert.Throws<InvalidOperationException>(() => execution.DescribeCapability());
        execution.Calculate(model.Combos, model.Groups);
        CalculationCapability capability = execution.DescribeCapability();
        long spent = budget.Spent;
        Assert.That(execution.DescribeCapability(), Is.EqualTo(capability));
        Assert.That(budget.Spent, Is.EqualTo(spent));
        Assert.That(capability.Reason, Is.EqualTo(reason));
        Assert.That(capability.Accuracy, Is.EqualTo(PhysicalSearchAccuracy.OptimisticSurvivalUnproven));
        Assert.That(capability.Certification, Is.EqualTo(support));
    }
    [Test]
    public void OptionalWorkExhaustionKeepsCompletedPrimaryAndPublishesNoPartialBounds() {
        EngineCase fixture = new("limited", new([
            new("search", 1, new(Search: new(new("target")))),
            new("target", 1), new("filler", 2),
            new("draw", 1, new(DrawCount: 1, HiddenPostDrawMill: 1))
        ], 2, [new("goal", [Requirement.InMembership("target")])], [new("target", ["target"])]), new());
        PreparedCalculation model = fixture.Prepare();
        WorkBudget measured = new(CalculationWorkPolicy.Interactive);
        DrawEffectCalculation reference = new(model.Cards, 2, measured);
        ExactCalculationResult expected = reference.Calculate(model.Combos, model.Groups);
        WorkBudget limited = new(new(measured.Spent));
        DrawEffectCalculation execution = new(model.Cards, 2, limited);
        ExactCalculationResult actual = execution.Calculate(model.Combos, model.Groups);
        SearchAvailabilityCertificate certificate = SearchAvailabilityCertification.TryCalculate(model.Combos, model.Groups, actual, limited, execution);
        Assert.That(actual.Total, Is.EqualTo(expected.Total));
        Assert.That(actual.Combos, Is.EqualTo(expected.Combos));
        Assert.That(certificate.Status, Is.EqualTo(SearchAvailabilityStatus.Limited));
        Assert.That(certificate.Total, Is.Null);
        Assert.That(certificate.Combos, Is.Empty);
        Assert.That(certificate.Detail, Does.Contain("Work"));
    }
}
