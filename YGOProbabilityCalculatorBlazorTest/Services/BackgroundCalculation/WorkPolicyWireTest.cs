using System.Text.Json;
using System.Text.Json.Nodes;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazorTest.Services.BackgroundCalculation;

[TestFixture]
public class WorkPolicyWireTest
{
    private static CalculationSnapshot Snapshot(CalculationWorkPolicy? policy = null) =>
        CalculationSnapshot.Capture(
            [new Card([], 2, id: "card")],
            [new Combo([], cards: [new("card", 1, 1)])],
            1,
            [],
            policy
        );

    [Test]
    public void WorkReasonAndInteractivePolicySurviveSourceGeneratedWire()
    {
        string response = CalculationWire.Execute(Snapshot(new(1)).Json);
        ProbabilityCalculationLimitException? error =
            Assert.Throws<ProbabilityCalculationLimitException>(() =>
                CalculationWire.ReadResult(response)
            );
        Assert.That(error!.Reason, Is.EqualTo(ProbabilityCalculationLimitReason.Work));
        Assert.That(
            CalculationWire.ReadResult(CalculationWire.Execute(Snapshot().Json)).TotalProbability,
            Is.EqualTo(1)
        );
        Assert.That(
            JsonNode.Parse(Snapshot(CalculationWorkPolicy.Interactive).Json)![
                "WorkUnits"
            ]!.GetValue<long>(),
            Is.EqualTo(CalculationWorkPolicy.Interactive.WorkUnits)
        );
        Assert.That(
            JsonNode.Parse(Snapshot().Json)!["WorkUnits"]!.GetValue<long>(),
            Is.EqualTo(CalculationWorkPolicy.Default.WorkUnits)
        );
    }

    [Test]
    public void StorageReasonSurvivesWireWithAmpleWork()
    {
        CategoryBase[] categories =
        [
            .. Enumerable.Range(0, 16).Select(i => new CategoryBase($"R{i}")),
        ];
        CalculationSnapshot snapshot = CalculationSnapshot.Capture(
            categories.Select(c => new Card([c], 2)),
            [new Combo(categories.Select(c => new ComboCategory(c, 1, 16)))],
            16,
            [],
            new(5_000_000)
        );
        ProbabilityCalculationLimitException? error =
            Assert.Throws<ProbabilityCalculationLimitException>(() =>
                CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json))
            );
        Assert.That(error!.Reason, Is.EqualTo(ProbabilityCalculationLimitReason.Storage));
    }

    [TestCase("0")]
    [TestCase("-1")]
    [TestCase("9223372036854775808")]
    [TestCase("\"unlimited\"")]
    [TestCase("1.5")]
    public void MalformedOrInvalidAllowancesNeverDisableLimits(string value)
    {
        JsonNode input = JsonNode.Parse(Snapshot().Json)!;
        input["WorkUnits"] = JsonNode.Parse(value);
        string response = CalculationWire.Execute(input.ToJsonString());
        Assert.That(
            () => CalculationWire.ReadResult(response),
            Throws.Exception.TypeOf<ArgumentException>().Or.TypeOf<InvalidOperationException>()
        );
        Assert.That(JsonNode.Parse(response)!["LimitReason"], Is.Null);
    }

    [Test]
    public void MissingAllowanceUsesBoundedDefaultAndCompatibilityRemainsAnInputFailure()
    {
        JsonObject input = JsonNode.Parse(Snapshot().Json)!.AsObject();
        input.Remove("WorkUnits");
        Assert.That(
            CalculationWire
                .ReadResult(CalculationWire.Execute(input.ToJsonString()))
                .TotalProbability,
            Is.EqualTo(1)
        );
        CalculationSnapshot snapshot = CalculationSnapshot.Capture(
            [],
            Enumerable.Range(0, 31).Select(_ => new Combo([])),
            1,
            []
        );
        Assert.Throws<ArgumentException>(() =>
            CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json))
        );
    }

    [TestCase("{}")]
    [TestCase("{\"Result\":{}}")]
    [TestCase("{\"Error\":\"unknown\",\"LimitReason\":99}")]
    [TestCase("{\"Error\":\"unknown\",\"FailureKind\":99}")]
    [TestCase("{\"LimitReason\":1}")]
    [TestCase("{\"Error\":\"conflict\",\"LimitReason\":1,\"FailureKind\":1}")]
    public void InvalidResponsesNeverMasqueradeAsResourceLimits(string json) =>
        Assert.Throws<InvalidOperationException>(() => CalculationWire.ReadResult(json));

    [Test]
    public void GenericErrorsRemainGenericAndConflictingSuccessIsRejected()
    {
        Assert.Throws<InvalidOperationException>(() =>
            CalculationWire.ReadResult("{\"Error\":\"ordinary failure\",\"FailureKind\":2}")
        );
        JsonNode success = JsonNode.Parse(CalculationWire.Execute(Snapshot().Json))!;
        success["Error"] = "conflicting error";
        Assert.Throws<InvalidOperationException>(() =>
            CalculationWire.ReadResult(success.ToJsonString())
        );
        Assert.Throws<JsonException>(() => CalculationWire.ReadResult("not JSON"));
    }
}
