// Measurement-only external Compile include for both frozen base and candidate.
// Never include this probe in a production publish. MeasureCertificate=false
// isolates primary Fast work; true uses the real public completion path.
// ADAPTIVE_FAST is measurement-only: define it only for adaptive candidate builds.
using System.Diagnostics;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;

public static partial class FastCertificateBenchmarkProbe {
    [JSExport, SupportedOSPlatform("browser")]
    public static string Measure(string json) {
        using JsonDocument document = JsonDocument.Parse(json);
        CalculationInput input = document.RootElement.GetProperty("Input").Deserialize(CalculationJsonContext.Default.CalculationInput)!;
        bool certify = document.RootElement.GetProperty("MeasureCertificate").GetBoolean();
        List<Card> cards = input.Cards.Select(card => new Card(card.Categories, card.Copies, card.Name, true,
            card.Id, card.ExternalCardId, card.ManualMetadataCategoryKeys, card.DrawCount, card.DrawOncePerTurn,
            card.PostDrawDiscardCount, card.PostDrawSelector, card.PostDrawFailureBehavior, card.PreDrawRemovalCount,
            card.PreDrawSelector, card.PreDrawDeckRemovalCount, card.EffectProfile, card.PostDrawDeckMillCount,
            card.FirstActivationOnly, card.BlocksLaterDraws, card.Search, card.OncePerTurn)).ToList();
        List<Combo> combos = input.Combos.Select(combo => new Combo(combo.Categories, combo.Name, true,
            combo.GroupId, combo.Cards, combo.AlternativeGroups)).ToList();
        WorkBudget budget = new(new(input.WorkUnits ?? CalculationWorkPolicy.Default.WorkUnits),
            CalculationStoragePolicy.ForTier(input.StorageTier ?? CalculationStorageTier.Standard));
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        long started = Stopwatch.GetTimestamp();
        string outcome = "Success";
        ProbabilityCalculationResult? result = null;
        ExactCalculationResult? exact = null;
        string? route = null;
        try {
            DrawEffectCalculation? execution = cards.Any(card => card.HasEffect)
                ? new(cards, input.HandSize, budget, input.Options?.DrawActivationPriority, input.Options?.Mode == CalculationModel.Exact
#if ADAPTIVE_FAST
                    , adaptiveFast: input.Options?.Mode != CalculationModel.Exact
#endif
                ) : null;
            exact = execution is null
                ? new ProbabilityCalculation(cards, input.HandSize, budget).CalculateExactResults(combos, input.Groups)
                : execution.Calculate(combos, input.Groups);
            result = certify
                ? ProbabilityCalculatorService.CompleteResult(combos, input.Groups, input.Options?.Mode ?? CalculationModel.Fast, exact, budget, execution)
                : exact.ToPublic(combos, input.Groups, budget);
            route = (execution?.DescribeCapability() ?? ProbabilityCalculation.Capability).Route.ToString();
        }
        catch (ProbabilityCalculationLimitException exception) {
            outcome = exception.Reason + "Limit";
        }
        double milliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        return JsonSerializer.Serialize(new FastCertificateMeasurement(milliseconds, allocated, outcome, route, budget.Spent, budget.PeakEntries, budget.PeakCells,
            exact?.Total.ToFraction(), exact?.Combos.Select(value => value.ToFraction()).ToArray(), exact?.Groups.Select(value => value.ToFraction()).ToArray(), result),
            FastCertificateMeasurementJsonContext.Default.FastCertificateMeasurement);
    }
}

internal sealed record FastCertificateMeasurement(double Milliseconds, long Allocated, string Outcome, string? Route, long Work, long Entries, long Cells,
    ProbabilityFraction? Total, ProbabilityFraction[]? Combos, ProbabilityFraction[]? Groups, ProbabilityCalculationResult? Result);

[JsonSerializable(typeof(FastCertificateMeasurement))]
internal partial class FastCertificateMeasurementJsonContext : JsonSerializerContext;
