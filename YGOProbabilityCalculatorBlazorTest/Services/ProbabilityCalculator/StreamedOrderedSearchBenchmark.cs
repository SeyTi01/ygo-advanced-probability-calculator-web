using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.Session;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture, Explicit("Full frozen hard-case public contracts; opt-in, no timing assertions"), NonParallelizable]
public sealed class StreamedOrderedSearchBenchmark {
    [Test]
    public async Task FrozenFastAndProgressiveContracts() {
        SessionState state = await SearchAvailabilityCertificateTest.Example("mill2");
        for (int row = 0; row < state.Cards.Count; row++) {
            Card card = state.Cards[row];
            if (card.DrawCount is not null) {
                state.Cards[row] = new(card.Categories, card.Copies, card.Name, id: card.Id, drawCount: card.DrawCount,
                    postDrawDiscardCount: 1, postDrawDeckMillCount: card.PostDrawDeckMillCount);
            }
        }
        CalculationSnapshot frozen = CalculationSnapshot.Capture(state.Cards, state.Combos, state.HandSize, state.ComboGroups,
            CalculationWorkPolicy.Default, options: new(state.DrawActivationPriority, Mode: CalculationModel.Exact));
        byte[] bytes = Encoding.UTF8.GetBytes(frozen.Json);
        Assert.That(bytes, Has.Length.EqualTo(33_087));
        Assert.That(Convert.ToHexStringLower(SHA256.HashData(bytes)),
            Is.EqualTo("3e5c5ac9d01a3318e7b801d5bd513645a3266dfbc23d564a972ff6d40c9a4b64"));
        CalculationInput original = JsonSerializer.Deserialize(frozen.Json, CalculationJsonContext.Default.CalculationInput)!;
        CalculationInput fast = original with { Options = original.Options! with { Mode = CalculationModel.Fast } };
        string bounded = JsonSerializer.Serialize(fast, CalculationJsonContext.Default.CalculationInput);
        CalculationResponse limited = JsonSerializer.Deserialize(CalculationWire.Execute(bounded), CalculationJsonContext.Default.CalculationResponse)!;
        Assert.That(limited.Result, Is.Null);
        Assert.That(limited.Contract!.Completion, Is.EqualTo(CalculationCompletion.WorkLimit));
        Assert.That(limited.Contract.Fractions, Is.Null);
        Assert.That(limited.Contract.AutomaticExact, Is.Null);

        string interactive = JsonSerializer.Serialize(fast with { WorkUnits = CalculationWorkPolicy.Interactive.WorkUnits },
            CalculationJsonContext.Default.CalculationInput);
        ProbabilityCalculationResult result = CalculationWire.ReadResult(CalculationWire.Execute(interactive), CalculationModel.Fast);
        string[] expected = ["16824355/19575738", "2414963/5593068", "50134/466089", "2078435/8700328",
            "1708107/8700328", "1117169/39151476", "39020131/78302952", "1542041/39151476", "524971/13050492",
            "50099687/78302952", "1435981/2796534"];
        Assert.That(Fractions(result), Is.EqualTo(expected));
        Assert.That(result.Contract!.RequestedMode, Is.EqualTo(CalculationModel.Fast));
        Assert.That(result.Contract.ExecutedMode, Is.EqualTo(CalculationModel.Fast));
        Assert.That(result.Contract.Accuracy, Is.EqualTo(CalculationAccuracy.Bounded));
        Assert.That(result.Contract.AutomaticExact, Is.Null);
        Assert.That(result.SearchAvailability!.Detail, Does.Not.Contain("Limited"));

        AutomaticFastStage initial = JsonSerializer.Deserialize(CalculationWire.ExecuteFastStage(interactive),
            CalculationJsonContext.Default.AutomaticFastStage)!;
        Assert.That(Fractions(CalculationWire.ReadResult(initial.Response, CalculationModel.Fast)), Is.EqualTo(expected));
        AutomaticFastStage upgrade = JsonSerializer.Deserialize(CalculationWire.ExecuteProgressiveUpgrade(interactive),
            CalculationJsonContext.Default.AutomaticFastStage)!;
        ProbabilityCalculationResult exact = CalculationWire.ReadResult(upgrade.Response, CalculationModel.Fast);
        string[] expectedExact = ["2960954609/3445329888", "283323827/656253312", "35288443/328126656", "4813141/20148128",
            "81985915/417615744", "393220481/13781319552", "3433125811/6890659776", "2591513/66256344", "184778005/4593773184",
            "2939033347/4593773184", "7074649735/13781319552"];
        Assert.That(Fractions(exact), Is.EqualTo(expectedExact));
        Assert.That(exact.Contract!.Accuracy, Is.EqualTo(CalculationAccuracy.Exact));
        // The worker transport adds Completed only after accepting this response.
        Assert.That(exact.Contract.AutomaticExact, Is.Null);
        string? output = Environment.GetEnvironmentVariable("YGO_STREAMED_RESULTS");
        if (output is not null) {
            File.WriteAllText(output, JsonSerializer.Serialize(new { Fast = result, Exact = exact }));
        }
    }

    private static string[] Fractions(ProbabilityCalculationResult result) {
        CalculationFractions fractions = result.Contract!.Fractions!;
        ProbabilityFraction[] rows = [fractions.Total, .. fractions.Combos, .. fractions.Groups];
        return rows.Select(value => value.Numerator + "/" + value.Denominator).ToArray();
    }
}
