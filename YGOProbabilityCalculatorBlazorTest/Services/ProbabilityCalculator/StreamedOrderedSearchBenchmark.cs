using System.Numerics;
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
        CalculationInput original = await FrozenInput();
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

    [Test]
    public async Task FrozenTwoCopyFastExactAndProgressiveContracts() {
        CalculationInput original = await FrozenInput();
        const string sourceId = "782b979b7aa046dc80a57026c14c8f7c";
        const string inertId = "certificate-inert-fuwalos";
        WorkerCard originalSource = original.Cards.Single(card => card.DrawCount is not null);
        WorkerCard originalInert = original.Cards.Single(card => card.Id == inertId);
        Assert.That(originalSource.Id, Is.EqualTo(sourceId));
        Assert.That(originalSource.Copies, Is.EqualTo(1));
        Assert.That(originalInert.Copies, Is.EqualTo(2));
        Assert.That(originalInert.DrawCount, Is.Null);

        CalculationInput two = original with {
            Cards = original.Cards.Select(card => card.Id switch {
                sourceId => card with { Copies = 2 },
                inertId => card with { Copies = 1 },
                _ => card
            }).ToArray(),
            WorkUnits = CalculationWorkPolicy.Interactive.WorkUnits
        };
        Assert.That(two.Cards.Sum(card => card.Copies), Is.EqualTo(40));
        Assert.That(two.HandSize, Is.EqualTo(5));
        Assert.That(two.Combos, Has.Length.EqualTo(8));
        Assert.That(two.Groups, Has.Length.EqualTo(2));
        Assert.That(two.StorageTier ?? CalculationStorageTier.Standard, Is.EqualTo(CalculationStorageTier.Standard));
        for (int row = 0; row < two.Cards.Length; row++) {
            Assert.That(two.Cards[row] with { Copies = original.Cards[row].Copies }, Is.EqualTo(original.Cards[row]),
                "The neighbor changes physical copies only; every saved plan and identity remains unchanged.");
        }
        WorkerCard source = two.Cards.Single(card => card.Id == sourceId);
        int allowance = source.ActivationLimit ?? ((source.OncePerTurn ?? source.DrawOncePerTurn) ? 1 : int.MaxValue);
        Assert.That(source.Copies, Is.EqualTo(2));
        Assert.That(allowance, Is.EqualTo(1), "Two physical copies retain the saved single activation allowance.");
        Assert.That(source.DrawCount, Is.EqualTo(2));
        Assert.That(source.PostDrawDiscardCount, Is.EqualTo(1));
        Assert.That(source.PostDrawDeckMillCount, Is.EqualTo(2));

        CalculationInput fastInput = two with { Options = two.Options! with { Mode = CalculationModel.Fast } };
        string fastJson = JsonSerializer.Serialize(fastInput, CalculationJsonContext.Default.CalculationInput);
        string exactJson = JsonSerializer.Serialize(two, CalculationJsonContext.Default.CalculationInput);
        string legacyJson = JsonSerializer.Serialize(fastInput with { WorkUnits = CalculationWorkPolicy.Default.WorkUnits },
            CalculationJsonContext.Default.CalculationInput);
        AssertHash(fastJson, 33_088, "4c634bf9599d20e86c6fd2cb9810904356f39dc8468382c99e49da1389ecbf28");
        AssertHash(exactJson, 33_088, "f43e90701698291cbf55da09cc5a3b8c45afb51cbb533a6a4b9c193f793e704b");
        AssertHash(legacyJson, 33_087, "22f05b9d1d506773289e51e81dfe26e4c770bb0356132265c310c6d2dc36b1df");
        CalculationResponse legacy = JsonSerializer.Deserialize(CalculationWire.Execute(legacyJson),
            CalculationJsonContext.Default.CalculationResponse)!;
        Assert.That(legacy.Result, Is.Null);
        Assert.That(legacy.LimitReason, Is.EqualTo(ProbabilityCalculationLimitReason.Work));
        Assert.That(legacy.Contract!.Completion, Is.EqualTo(CalculationCompletion.WorkLimit));
        Assert.That(legacy.Contract.Fractions, Is.Null);

        ProbabilityCalculationResult fast = CalculationWire.ReadResult(CalculationWire.Execute(fastJson), CalculationModel.Fast);
        ProbabilityCalculationResult exact = CalculationWire.ReadResult(CalculationWire.ExecuteInteractiveExact(exactJson), CalculationModel.Exact);
        // Completed physical enumeration on pre-change dev a77974905840858eb258dcebc798667665a0c2d0.
        // These eleven reference rows were established before the multi-copy reduction.
        string[] expectedExact = ["956008117/1087998912", "3089590445/6890659776", "1201021741/10335989664",
            "5109740765/20671979328", "4373111233/20671979328", "109424279/3445329888", "1185033191/2296886592",
            "15660367/369142488", "26616179/574221648", "6855759089/10335989664", "11000857495/20671979328"];
        Assert.That(Fractions(exact), Is.EqualTo(expectedExact));
        Assert.That(exact.Contract!.RequestedMode, Is.EqualTo(CalculationModel.Exact));
        Assert.That(exact.Contract.ExecutedMode, Is.EqualTo(CalculationModel.Exact));
        Assert.That(exact.Contract.Completion, Is.EqualTo(CalculationCompletion.Success));
        Assert.That(exact.Contract.Accuracy, Is.EqualTo(CalculationAccuracy.Exact));
        AssertCertified(fast, exact);

        AutomaticFastStage initial = JsonSerializer.Deserialize(CalculationWire.ExecuteFastStage(fastJson),
            CalculationJsonContext.Default.AutomaticFastStage)!;
        ProbabilityCalculationResult progressiveFast = CalculationWire.ReadResult(initial.Response, CalculationModel.Fast);
        Assert.That(Fractions(progressiveFast), Is.EqualTo(Fractions(fast)));
        AssertCertified(progressiveFast, exact);
        AutomaticFastStage upgrade = JsonSerializer.Deserialize(CalculationWire.ExecuteProgressiveUpgrade(fastJson),
            CalculationJsonContext.Default.AutomaticFastStage)!;
        ProbabilityCalculationResult progressiveExact = CalculationWire.ReadResult(upgrade.Response, CalculationModel.Fast);
        Assert.That(Fractions(progressiveExact), Is.EqualTo(expectedExact));
        Assert.That(progressiveExact.Contract!.RequestedMode, Is.EqualTo(CalculationModel.Fast));
        // The pre-change Progressive wire retains the Fast mode header; physical
        // execution is established by Exact accuracy, route, proof and fractions.
        Assert.That(progressiveExact.Contract.ExecutedMode, Is.EqualTo(CalculationModel.Fast));
        Assert.That(progressiveExact.Contract.SolverRoute, Is.EqualTo(exact.Contract.SolverRoute));
        Assert.That(progressiveExact.Contract.Proof, Is.EqualTo(exact.Contract.Proof));
        Assert.That(progressiveExact.SearchAvailability, Is.Null);
        Assert.That(progressiveExact.Contract.Completion, Is.EqualTo(CalculationCompletion.Success));
        Assert.That(progressiveExact.Contract.Accuracy, Is.EqualTo(CalculationAccuracy.Exact));
        // Acceptance and Completed ownership belong to the worker transport.
        Assert.That(progressiveExact.Contract.AutomaticExact, Is.Null);

        string? output = Environment.GetEnvironmentVariable("YGO_MULTICOPY_STREAMED_RESULTS");
        if (output is not null) {
            File.WriteAllText(output, JsonSerializer.Serialize(new {
                Fast = fast, Exact = exact, ProgressiveFast = progressiveFast, ProgressiveExact = progressiveExact, Legacy = legacy
            }));
        }
    }

    private static async Task<CalculationInput> FrozenInput() {
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
        AssertHash(frozen.Json, 33_087, "3e5c5ac9d01a3318e7b801d5bd513645a3266dfbc23d564a972ff6d40c9a4b64");
        return JsonSerializer.Deserialize(frozen.Json, CalculationJsonContext.Default.CalculationInput)!;
    }

    private static void AssertHash(string json, int length, string hash) {
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        Assert.That(bytes, Has.Length.EqualTo(length));
        Assert.That(Convert.ToHexStringLower(SHA256.HashData(bytes)), Is.EqualTo(hash));
    }

    private static void AssertCertified(ProbabilityCalculationResult fast, ProbabilityCalculationResult exact) {
        Assert.That(fast.Contract!.RequestedMode, Is.EqualTo(CalculationModel.Fast));
        Assert.That(fast.Contract.ExecutedMode, Is.EqualTo(CalculationModel.Fast));
        Assert.That(fast.Contract.Completion, Is.EqualTo(CalculationCompletion.Success));
        Assert.That(fast.Contract.Accuracy, Is.EqualTo(CalculationAccuracy.Bounded));
        Assert.That(fast.Contract.AutomaticExact, Is.Null);
        Assert.That(fast.ComboProbabilities, Has.Count.EqualTo(8));
        Assert.That(fast.GroupProbabilities, Has.Count.EqualTo(2));
        SearchAvailabilityCertificate certificate = fast.SearchAvailability!;
        Assert.That(certificate.Status, Is.EqualTo(SearchAvailabilityStatus.Bounded));
        Assert.That(certificate.Detail, Does.Not.Contain("Limited"));
        Assert.That(certificate.Combos, Has.Length.EqualTo(8));
        Assert.That(certificate.Groups, Has.Length.EqualTo(2));
        SearchAvailabilityInterval[] intervals = [certificate.Total!, .. certificate.Combos, .. certificate.Groups];
        CalculationFractions fractions = exact.Contract!.Fractions!;
        ProbabilityFraction[] rows = [fractions.Total, .. fractions.Combos, .. fractions.Groups];
        for (int row = 0; row < rows.Length; row++) {
            BigInteger numerator = BigInteger.Parse(rows[row].Numerator);
            BigInteger denominator = BigInteger.Parse(rows[row].Denominator);
            Assert.That(BigInteger.Parse(intervals[row].Lower.Numerator) * denominator,
                Is.LessThanOrEqualTo(numerator * BigInteger.Parse(intervals[row].Lower.Denominator)), $"lower row {row}");
            Assert.That(BigInteger.Parse(intervals[row].Upper.Numerator) * denominator,
                Is.GreaterThanOrEqualTo(numerator * BigInteger.Parse(intervals[row].Upper.Denominator)), $"upper row {row}");
        }
    }

    private static string[] Fractions(ProbabilityCalculationResult result) {
        CalculationFractions fractions = result.Contract!.Fractions!;
        ProbabilityFraction[] rows = [fractions.Total, .. fractions.Combos, .. fractions.Groups];
        return rows.Select(value => value.Numerator + "/" + value.Denominator).ToArray();
    }
}
