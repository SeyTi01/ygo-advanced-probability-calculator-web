using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture, Explicit("One frozen cheap challenge exact reference; no heavy Exact"), NonParallelizable]
public class DangerChallengeReference {
    [Test]
    public void CaptureCheapChallenge() {
        string directory = Environment.GetEnvironmentVariable("YGO_DANGER_RESEARCH")!;
        using JsonDocument fixtures = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "frozen-fixtures.json")));
        JsonElement fixture = fixtures.RootElement.GetProperty("Cases").EnumerateArray()
            .Single(value => value.GetProperty("Name").GetString() == "challenge-mixed-40-direct-exclusion");
        JsonNode input = JsonNode.Parse(fixture.GetProperty("Input").GetRawText())!;
        input["Options"]!["Mode"] = (int)CalculationModel.Exact;
        input["WorkUnits"] = 150_000_000L;
        Stopwatch timer = Stopwatch.StartNew();
        // Execute's bounded harness policy is intentional: this diagnostic may
        // fail at150M and cannot accidentally start an unlimited heavy run.
        string response = CalculationWire.Execute(input.ToJsonString());
        timer.Stop();
        File.WriteAllText(Path.Combine(directory, "cheap-challenge-reference.json"), JsonSerializer.Serialize(new {
            Name = "challenge-mixed-40-direct-exclusion",
            FrozenInputSha256 = fixture.GetProperty("InputSha256").GetString(),
            ReferenceMethod = "Unchanged physical Exact route, bounded diagnostic request at150M Work",
            Milliseconds = timer.Elapsed.TotalMilliseconds,
            Response = JsonSerializer.Deserialize<JsonElement>(response)
        }));
        ProbabilityCalculationResult result = CalculationWire.ReadResult(response, CalculationModel.Exact);
        Assert.That(result.Contract!.Accuracy, Is.EqualTo(CalculationAccuracy.Exact));
        Assert.That(result.Contract.Fractions, Is.Not.Null);
    }
}
