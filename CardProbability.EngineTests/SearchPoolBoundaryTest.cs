using CardProbability.Internal;
using CardProbability.Internal.DrawResolution;
using CardProbability.Internal.Search;
using C = CardProbability.Internal.Contracts;
using M = CardProbability.Internal.Model;
using Budget = CardProbability.Internal.Resources.WorkBudget;

namespace CardProbability.EngineTests;

public class SearchPoolBoundaryTest {
    [TestCase(false)]
    [TestCase(true)]
    public void IndependentDrawEntriesRetainCountStateExecution(bool once) {
        EffectPlan draw = new(DrawCount: 1, ActivationLimit: once ? 1 : null);
        CalculationRequest request = new(
            [new("first", 1, draw), new("second", 1, draw), new("target", 2), new("blank", 2)],
            2, [new("goal", [Requirement.OfEntry("target")])]);
        PreparedCalculation prepared = PreparedCalculation.Create(request, new(), new(C.CalculationWorkPolicy.Unlimited));

        Assert.That(DrawExecutionAssessment.Assess(prepared.Cards, prepared.Combos, 2, true).Path,
            Is.EqualTo(DrawExecutionPath.DrawCountState));
    }

    [TestCase(0)]
    [TestCase(1)]
    public void HeldSourceAccessIsBoundedByPhysicalCapacity(int capacity) {
        CalculationRequest request = new(
            [new("source", 1, new(Search: new(new("target")))), new("target", 1), new("blank", 1)],
            0, [new("goal", [Requirement.OfEntry("target")])], [new("target", ["target"])]);
        Budget budget = new(C.CalculationWorkPolicy.Unlimited);
        PreparedCalculation prepared = PreparedCalculation.Create(request, new(), budget);
        ExactCalculationResult result = new SearchProbabilityCalculation(
            [], 0, [prepared.Cards[0]], new(prepared.Cards, budget), capacity, budget
        ).Calculate(prepared.Combos, prepared.Groups, false);

        Assert.That(result.Total.Numerator, Is.EqualTo(new System.Numerics.BigInteger(capacity)));
        Assert.That(result.Total.Denominator, Is.EqualTo(System.Numerics.BigInteger.One));
    }

    [Test]
    public void UniformFinalPoolUsesItsConditionalSubsetDistribution() {
        CalculationRequest request = new(
            [new("source", 1, new(Search: new(new("target")))), new("target", 2), new("blank", 1)],
            0, [new("goal", [Requirement.OfEntry("target", 2, 2)], groupId: "g")],
            [new("target", ["target"])], ["g", "empty"]);
        Budget budget = new(C.CalculationWorkPolicy.Unlimited);
        PreparedCalculation prepared = PreparedCalculation.Create(request, new(), budget);
        List<M.Card> uniformPool = [prepared.Cards[0], prepared.Cards[2]];
        ExactCalculationResult result = new SearchProbabilityCalculation(
            uniformPool, 1, [prepared.Cards[1].WithCopies(1)], new(prepared.Cards, budget), 1, budget,
            [new(uniformPool, 1)]
        ).Calculate(prepared.Combos, prepared.Groups, false);

        // The random pool supplies the source or a blank with equal probability.
        // Only the source can fetch the second target alongside the fixed target.
        Assert.That(result.Total.Numerator * 2, Is.EqualTo(result.Total.Denominator));
        Assert.That(result.Combos[0], Is.EqualTo(result.Total));
        Assert.That(result.Groups[0], Is.EqualTo(result.Total));
        Assert.That(result.Groups[1].Numerator.IsZero, Is.True);
    }

    [TestCase(1, false)]
    [TestCase(2, true)]
    [TestCase(null, true)]
    public void HeldCopiesShareAnEntryAllowance(int? limit, bool succeeds) {
        CalculationRequest request = new(
            [new("source", 2, new(ActivationLimit: limit, Search: new(new("target")))), new("target", 2)],
            0, [new("goal", [Requirement.OfEntry("target", 2, 2)])], [new("target", ["target"])]);
        Budget budget = new(C.CalculationWorkPolicy.Unlimited);
        PreparedCalculation prepared = PreparedCalculation.Create(request, new(), budget);
        ExactCalculationResult result = new SearchProbabilityCalculation(
            [], 0, [prepared.Cards[0]], new(prepared.Cards, budget), 2, budget
        ).Calculate(prepared.Combos, prepared.Groups, false);

        Assert.That(result.Total.Numerator.IsZero, Is.EqualTo(!succeeds));
        Assert.That(result.Total.Denominator, Is.EqualTo(System.Numerics.BigInteger.One));
    }

    [Test]
    public void DifferentEntriesHaveIndependentAllowancesForTheSameTargets() {
        EffectPlan search = new(Search: new(new("target")));
        CalculationRequest request = new(
            [new("first", 1, search), new("second", 1, search), new("target", 2)],
            0, [new("goal", [Requirement.OfEntry("target", 2, 2)])], [new("target", ["target"])]);
        Budget budget = new(C.CalculationWorkPolicy.Unlimited);
        PreparedCalculation prepared = PreparedCalculation.Create(request, new(), budget);
        ExactCalculationResult result = new SearchProbabilityCalculation(
            [], 0, prepared.Cards.Take(2).ToList(), new(prepared.Cards, budget), 2, budget
        ).Calculate(prepared.Combos, prepared.Groups, false);

        Assert.That(result.Total.Numerator, Is.EqualTo(result.Total.Denominator));
    }

    [Test]
    public void OrderedFactorialStorageIsBoundedBeforeCounting() {
        CalculationRequest request = new(
            [new("source", 1, new(DrawCount: 2, RemovalCount: 1)), new("target", 1), new("blank", 3_000)],
            2, [new("goal", [Requirement.OfEntry("target")])]);
        Budget budget = new(C.CalculationWorkPolicy.Unlimited);
        PreparedCalculation prepared = PreparedCalculation.Create(request, new(), budget);
        C.ProbabilityCalculationLimitException error = Assert.Throws<C.ProbabilityCalculationLimitException>(() =>
            new PostDrawCalculation(prepared.Cards, 2, budget).Calculate(prepared.Combos, prepared.Groups))!;
        Assert.That(error.Reason, Is.EqualTo(C.ProbabilityCalculationLimitReason.Storage));
    }

    [Test]
    public void RaisedStoragePolicyCannotOverflowPopulationSizedArrays() {
        CalculationRequest request = new(
            [new("source", 1, new(DrawCount: 2, RemovalCount: 1)), new("target", 1), new("blank", int.MaxValue - 2)],
            2, [new("goal", [Requirement.OfEntry("target")])]);
        Budget Budget() => new(C.CalculationWorkPolicy.Unlimited, new(long.MaxValue, long.MaxValue));
        PreparedCalculation prepared = PreparedCalculation.Create(request, new(), Budget());

        C.ProbabilityCalculationLimitException ordered = Assert.Throws<C.ProbabilityCalculationLimitException>(() =>
            new PostDrawCalculation(prepared.Cards, 2, Budget()).Calculate(prepared.Combos, prepared.Groups))!;
        C.ProbabilityCalculationLimitException hiddenLoss = Assert.Throws<C.ProbabilityCalculationLimitException>(() =>
            new PhysicalSearchLossCounter(prepared.Cards, Budget()))!;

        Assert.That(ordered.Reason, Is.EqualTo(C.ProbabilityCalculationLimitReason.Storage));
        Assert.That(hiddenLoss.Reason, Is.EqualTo(C.ProbabilityCalculationLimitReason.Storage));
    }

    [TestCase(1)]
    [TestCase(2)]
    public void CapacityProofSelectsCountStateExchangeableOrInertModel(int mill) {
        CalculationRequest Request(EffectPlan effect, int hand) => new(
            [new("effect", 1, effect), new("target", 1), new("blank", 4)],
            hand, [new("goal", [Requirement.OfEntry("target", 1, 1)])]);
        PreparedCalculation countState = PreparedCalculation.Create(Request(new(DrawCount: 1, HiddenPostDrawMill: mill), 1), new(), new(C.CalculationWorkPolicy.Unlimited));
        PreparedCalculation exchangeable = PreparedCalculation.Create(Request(new(DrawCount: 2, ActivationLimit: null, RemovalCount: 1, HiddenPostDrawMill: mill), 1), new(), new(C.CalculationWorkPolicy.Unlimited));

        Assert.That(DrawExecutionAssessment.Assess(countState.Cards, countState.Combos, 1, true).Path, Is.EqualTo(DrawExecutionPath.DrawCountState));
        Assert.That(DrawExecutionAssessment.Assess(exchangeable.Cards, exchangeable.Combos, 1, true).Path, Is.EqualTo(DrawExecutionPath.ExchangeableConditional));
        Assert.That(DrawExecutionAssessment.Assess(exchangeable.Cards, exchangeable.Combos, 4, true).Path, Is.EqualTo(DrawExecutionPath.Ordinary));
    }
}
