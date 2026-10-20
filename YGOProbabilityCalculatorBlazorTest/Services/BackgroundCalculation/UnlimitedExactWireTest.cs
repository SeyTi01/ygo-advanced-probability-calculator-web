using System.Text.Json;
using System.Text.Json.Nodes;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.BackgroundCalculation;

[TestFixture]
public sealed class UnlimitedExactWireTest {
    private static string Input(CalculationModel mode, long work = 1) => CalculationSnapshot.Capture(
        ThreeCalculationModesTest.Counterexample("discard"), ThreeCalculationModesTest.Combos, 2,
        ThreeCalculationModesTest.Groups, new(work), options: new(Mode: mode)).Json;

    [Test]
    public void ExplicitEntryPointsIgnoreFiniteRemainderButCompatibilityCallsStayBounded() {
        string exact = Input(CalculationModel.Exact);
        string fast = Input(CalculationModel.Fast);
        Assert.Throws<ProbabilityCalculationLimitException>(() => CalculationWire.ReadResult(CalculationWire.Execute(exact)));
        Assert.Throws<ProbabilityCalculationLimitException>(() => CalculationWire.ReadResult(CalculationWire.Execute(fast)));
        AutomaticFastStage initial = JsonSerializer.Deserialize<AutomaticFastStage>(CalculationWire.ExecuteFastStage(fast))!;
        Assert.Throws<ProbabilityCalculationLimitException>(() => CalculationWire.ReadResult(initial.Response));
        ProbabilityCalculationResult manual = CalculationWire.ReadResult(CalculationWire.ExecuteInteractiveExact(exact), CalculationModel.Exact);
        AutomaticFastStage upgraded = JsonSerializer.Deserialize<AutomaticFastStage>(CalculationWire.ExecuteProgressiveUpgrade(fast))!;
        ProbabilityCalculationResult progressive = CalculationWire.ReadResult(upgraded.Response, CalculationModel.Fast);
        Assert.That(progressive.Contract!.Accuracy, Is.EqualTo(CalculationAccuracy.Exact));
        Assert.That(progressive.Contract.Fractions!.Total, Is.EqualTo(manual.Contract!.Fractions!.Total));
        Assert.That(progressive.Contract.Fractions.Combos, Is.EqualTo(manual.Contract.Fractions.Combos));
        Assert.That(progressive.Contract.Fractions.Groups, Is.EqualTo(manual.Contract.Fractions.Groups));
        SearchCertificateOracle.Counts oracle = SearchCertificateOracle.Enumerate(ThreeCalculationModesTest.Counterexample("discard"), 2,
            ThreeCalculationModesTest.Combos, ThreeCalculationModesTest.Groups);
        PhysicalSearchTest.AssertRows(manual, oracle.Wins[1], oracle.Outcomes);
    }

    [TestCase(CalculationModel.Fast), TestCase(CalculationModel.OpeningHand)]
    public void InteractiveExactRejectsOtherModes(CalculationModel mode) =>
        Assert.Throws<ArgumentException>(() => CalculationWire.ReadResult(CalculationWire.ExecuteInteractiveExact(Input(mode))));

    [TestCase(CalculationModel.Exact), TestCase(CalculationModel.OpeningHand)]
    public void ProgressiveUpgradeRejectsOtherModes(CalculationModel mode) {
        AutomaticFastStage stage = JsonSerializer.Deserialize<AutomaticFastStage>(CalculationWire.ExecuteProgressiveUpgrade(Input(mode)))!;
        Assert.Throws<ArgumentException>(() => CalculationWire.ReadResult(stage.Response));
    }

    [TestCase("WorkUnits", "0"), TestCase("WorkUnits", "-1"), TestCase("WorkUnits", "\"unlimited\""),
     TestCase("Unlimited", "true"), TestCase("Execution", "1")]
    public void UnlimitedEntryPointStillValidatesFiniteSnapshot(string key, string value) {
        JsonObject input = JsonNode.Parse(Input(CalculationModel.Exact))!.AsObject();
        input[key] = JsonNode.Parse(value);
        Assert.Throws<ArgumentException>(() => CalculationWire.ReadResult(CalculationWire.ExecuteInteractiveExact(input.ToJsonString())));
    }

    [Test]
    public void UnlimitedPolicyCannotBecomeAnImplicitWireSentinel() =>
        Assert.Throws<InvalidOperationException>(() => CalculationSnapshot.Capture([], [], 0, [], CalculationWorkPolicy.Unlimited));
}
