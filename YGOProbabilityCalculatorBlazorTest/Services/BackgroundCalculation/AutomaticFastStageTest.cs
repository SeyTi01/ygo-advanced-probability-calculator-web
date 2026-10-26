using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.BackgroundCalculation;

[TestFixture]
public sealed class AutomaticFastStageTest {
    private static AutomaticFastStage Stage(string json, string? remaining = null) =>
        JsonSerializer.Deserialize<AutomaticFastStage>(CalculationWire.ExecuteFastStage(json, remaining))!;

    [TestCase("banish", 2), TestCase("mill", 2), TestCase("consumed", 1), TestCase("discard", 2)]
    public void EachStagePreservesReferenceRowsAndPhysicalOracleWithOneAllowance(string kind, int hand) {
        List<Card> cards = ThreeCalculationModesTest.Counterexample(kind);
        string json = CalculationSnapshot.Capture(cards, ThreeCalculationModesTest.Combos, hand, ThreeCalculationModesTest.Groups,
            CalculationWorkPolicy.Interactive).Json;
        AutomaticFastStage first = Stage(json);
        Assert.That(first.Response, Is.EqualTo(CalculationWire.Execute(json)), "completed fallback is unchanged");
        long remaining = long.Parse(first.RemainingWork, CultureInfo.InvariantCulture);
        Assert.That(remaining, Is.InRange(1, CalculationWorkPolicy.Interactive.WorkUnits - 1));
        AutomaticFastStage second = Stage(json, first.RemainingWork);
        Assert.That(long.Parse(second.RemainingWork, CultureInfo.InvariantCulture), Is.LessThan(remaining));
        ProbabilityCalculationResult actual = CalculationWire.ReadResult(second.Response, CalculationModel.Fast);
        ProbabilityCalculationResult exact = ThreeCalculationModesTest.Run(cards, hand, CalculationModel.Exact);
        Assert.Multiple(() => {
            Assert.That(actual.Contract!.RequestedMode, Is.EqualTo(CalculationModel.Fast));
            Assert.That(actual.Contract.ExecutedMode, Is.EqualTo(CalculationModel.Fast));
            Assert.That(actual.Contract.Accuracy, Is.EqualTo(CalculationAccuracy.Exact));
            Assert.That(actual.Contract.SolverRoute, Is.EqualTo(exact.Contract!.SolverRoute));
            Assert.That(actual.Contract.Fractions!.Total, Is.EqualTo(exact.Contract.Fractions!.Total));
            Assert.That(actual.Contract.Fractions.Combos, Is.EqualTo(exact.Contract.Fractions.Combos));
            Assert.That(actual.Contract.Fractions.Groups, Is.EqualTo(exact.Contract.Fractions.Groups));
        });
        SearchCertificateOracle.Counts oracle = SearchCertificateOracle.Enumerate(cards, hand,
            ThreeCalculationModesTest.Combos, ThreeCalculationModesTest.Groups);
        PhysicalSearchTest.AssertRows(actual, oracle.Wins[1], oracle.Outcomes);
        AutomaticFastStage limited = Stage(json, "1");
        Assert.Throws<ProbabilityCalculationLimitException>(() => CalculationWire.ReadResult(limited.Response, CalculationModel.Fast));
        Assert.That(first.Response, Is.EqualTo(CalculationWire.Execute(json)), "failed attempt cannot mutate fallback");
    }

    [TestCase("0"), TestCase("-1"), TestCase("150000001"), TestCase("invalid")]
    public void UpgradeCannotIncreaseOrDisableOriginalWork(string allowance) {
        string json = CalculationSnapshot.Capture([new([], 2, id: "a")], [new([], cards: [new("a", 1, 1)])], 1, [],
            CalculationWorkPolicy.Interactive).Json;
        Assert.Throws<ArgumentException>(() => CalculationWire.ReadResult(Stage(json, allowance).Response));
    }

    [TestCase(CalculationModel.Exact), TestCase(CalculationModel.OpeningHand)]
    public void AutomaticStagesCannotChangeExplicitModes(CalculationModel mode) {
        string json = CalculationSnapshot.Capture([new([], 2, id: "a")], [new([], cards: [new("a", 1, 1)])], 1, [],
            options: new(Mode: mode)).Json;
        Assert.Throws<ArgumentException>(() => CalculationWire.ReadResult(Stage(json).Response));
        Assert.That(CalculationWire.ReadResult(CalculationWire.Execute(json), mode).Contract!.ExecutedMode, Is.EqualTo(mode));
    }

    [Test]
    public void AutomaticOutcomeCannotFalselyClaimCompletedExactOrChangeMode() {
        string json = CalculationSnapshot.Capture(ThreeCalculationModesTest.Counterexample("banish"),
            ThreeCalculationModesTest.Combos, 2, ThreeCalculationModesTest.Groups).Json;
        JsonObject response = JsonNode.Parse(Stage(json).Response)!.AsObject();
        response["Result"]!["Contract"]!["AutomaticExact"] = 0;
        Assert.Throws<InvalidOperationException>(() => CalculationWire.ReadResult(response.ToJsonString(), CalculationModel.Fast));
        response["Result"]!["Contract"]!["AutomaticExact"] = 99;
        Assert.Throws<InvalidOperationException>(() => CalculationWire.ReadResult(response.ToJsonString(), CalculationModel.Fast));
    }
}
