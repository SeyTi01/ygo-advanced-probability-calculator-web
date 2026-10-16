using System.Numerics;
using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture, NonParallelizable]
public sealed class RefinementResearchOracle {
    [TestCase("minimal-hidden-only-target")]
    [TestCase("minimal-milled-only-target")]
    [TestCase("known-source-two-targets")]
    [TestCase("known-source-opt-False-searchable-True-hidden-1")]
    [TestCase("mixed-visible-hidden")]
    [TestCase("coalesced-uneven-targets")]
    [TestCase("deferred-loss-cost-True-first-False")]
    public void PhysicalLabeledOracleContainsEveryCheckpoint(string name) {
        SearchCertificateFixtures.Fixture fixture = SearchCertificateFixtures.Fixtures().Single(value => value.Name == name);
        SearchCertificateOracle.Counts oracle = SearchCertificateOracle.Enumerate(fixture.Deck, fixture.Hand, fixture.Combos, fixture.Groups, fixture.Priority);
        string input = CalculationSnapshot.Capture(fixture.Deck, fixture.Combos, fixture.Hand, fixture.Groups,
            CalculationWorkPolicy.Interactive, options: new(fixture.Priority is null ? null : new(DrawActivationMode.CustomPriority, fixture.Priority))).Json;
        foreach (int cap in new[] { 1, 100, 1000, 100000, 1000000 }) {
            string request = "{\"Input\":" + input + ",\"MeasureCertificate\":true,\"RefinementBudget\":" + cap + "}";
            using JsonDocument document = JsonDocument.Parse(FastCertificateBenchmarkProbe.Measure(request));
            JsonElement result = document.RootElement;
            Assert.That(result.GetProperty("Outcome").GetString(), Is.EqualTo("Success"));
            JsonElement refinement = result.GetProperty("Refinement");
            Assert.That(refinement.GetProperty("Status").GetString(), Does.Not.StartWith("ProofFailure"));
            List<JsonElement> intervals = [refinement, .. refinement.GetProperty("Checkpoints").EnumerateArray()];
            foreach (JsonElement point in intervals) {
                JsonElement[] lower = point.GetProperty("Lower").EnumerateArray().ToArray();
                JsonElement[] upper = point.GetProperty("Upper").EnumerateArray().ToArray();
                for (int row = 0; row < lower.Length; row++) {
                    (BigInteger ln, BigInteger ld) = Parse(lower[row]);
                    (BigInteger un, BigInteger ud) = Parse(upper[row]);
                    Assert.That(ln * oracle.Outcomes, Is.LessThanOrEqualTo(oracle.Wins[1][row] * ld), name + ":lower:" + row);
                    Assert.That(un * oracle.Outcomes, Is.GreaterThanOrEqualTo(oracle.Wins[1][row] * ud), name + ":upper:" + row);
                }
            }
        }
    }
    private static (BigInteger, BigInteger) Parse(JsonElement value) => (
        BigInteger.Parse(value.GetProperty("Numerator").GetString()!), BigInteger.Parse(value.GetProperty("Denominator").GetString()!));
}
