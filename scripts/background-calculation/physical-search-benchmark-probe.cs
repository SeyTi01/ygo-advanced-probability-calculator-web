// Compile externally into benchmark-only builds. PHYSICAL_BASELINE selects
// the dev implementation before physical search. Never ship this probe.
using System.Diagnostics;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;

public static partial class ModeBenchmarkProbe {
    [JSExport, SupportedOSPlatform("browser")]
    public static string Measure(string json) {
        CalculationInput input = JsonSerializer.Deserialize(json, CalculationJsonContext.Default.CalculationInput)!;
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
        try {
#if PHYSICAL_BASELINE
            DrawEffectCalculation? execution = cards.Any(card => card.HasEffect) ? new(cards, input.HandSize, budget, input.Options?.DrawActivationPriority) : null;
#else
            DrawEffectCalculation? execution = cards.Any(card => card.HasEffect)
                ? new(cards, input.HandSize, budget, input.Options?.DrawActivationPriority, input.Options?.Mode == CalculationModel.Exact) : null;
#endif
            exact = execution is null
                ? new ProbabilityCalculation(cards, input.HandSize, budget).CalculateExactResults(combos, input.Groups)
                : execution.Calculate(combos, input.Groups);
            result = ProbabilityCalculatorService.CompleteResult(combos, input.Groups, input.Options?.Mode ?? CalculationModel.Fast,
                exact, budget, execution);
        }
        catch (UnsupportedExactCalculationException) {
            outcome = "Unsupported";
        }
        catch (ProbabilityCalculationLimitException exception) {
            outcome = exception.Reason + "Limit";
        }
        double milliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        return JsonSerializer.Serialize(new ModeMeasurement(milliseconds, allocated, outcome, budget.Spent, budget.PeakEntries, budget.PeakCells,
            result is null ? null : exact!.Total.ToFractionForBenchmark(), result is null ? [] : exact!.Combos.Select(ToFractionForBenchmark).ToArray(),
            result is null ? [] : exact!.Groups.Select(ToFractionForBenchmark).ToArray(), result),
            ModeMeasurementJsonContext.Default.ModeMeasurement);
    }

    private static string ToFractionForBenchmark(this ExactProbability value) => $"{value.Numerator}/{value.Denominator}";
}

internal sealed record ModeMeasurement(double Milliseconds, long Allocated, string Outcome, long Work, long Entries, long Cells,
    string? Total, string[] Combos, string[] Groups, ProbabilityCalculationResult? Result);

[JsonSerializable(typeof(ModeMeasurement))]
internal partial class ModeMeasurementJsonContext : JsonSerializerContext;
