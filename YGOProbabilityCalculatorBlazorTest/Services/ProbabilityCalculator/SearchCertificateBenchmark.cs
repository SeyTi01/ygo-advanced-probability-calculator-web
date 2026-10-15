using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture, Explicit("Certificate paired native/worker fixture export"), NonParallelizable]
public sealed class SearchCertificateBenchmark {
    [Test]
    public async Task Export() {
        List<(string Name, SessionState Session)> cases = [];
        cases.Add(("ordinary", await UnionBenchmark.LoadModel()));
        SessionState countState = await UnionBenchmark.LoadModel();
        int index = countState.Cards.FindIndex(card => card.Name == "Mulcharmy Fuwalos");
        countState.Cards[index] = countState.Cards[index].WithDrawCount(2);
        cases.Add(("count-state", countState));
        cases.Add(("three-graceful", await RealisticGracefulFixture.Load()));
        cases.Add(("trade-allure", await TradeInAllureFixture.Load()));
        cases.Add(("hidden-desires", await RealisticMixedFixture.Load(false)));
        foreach (string name in new[] { "plain", "one-draw2", "three-draw2", "banish10", "mill2" }) {
            cases.Add((name, await SearchAvailabilityCertificateTest.Example(name)));
        }
        List<object> exports = [];
        foreach ((string name, SessionState session) in cases) {
            string input = CalculationSnapshot.Capture(session.Cards, session.Combos, session.HandSize, session.ComboGroups,
                CalculationWorkPolicy.Interactive).Json;
            exports.Add(new { Name = name, Input = JsonSerializer.Deserialize<JsonElement>(input) });
        }
        string output = Environment.GetEnvironmentVariable("YGO_CERTIFICATE_FIXTURES")
            ?? throw new InvalidOperationException("Set YGO_CERTIFICATE_FIXTURES to the output file.");
        File.WriteAllText(output, JsonSerializer.Serialize(exports));
    }
}
