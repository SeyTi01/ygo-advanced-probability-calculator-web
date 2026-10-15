// Compile into both benchmark-only app builds using an external MSBuild Compile
// include. Define MODE_BASELINE for the unchanged base. Never ship this probe.
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
        WorkBudget budget = new(CalculationWorkPolicy.Interactive);
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        long started = Stopwatch.GetTimestamp();
        DrawEffectCalculation? execution = cards.Any(card => card.HasEffect) ? new(cards, input.HandSize, budget, input.Options?.DrawActivationPriority) : null;
        ExactCalculationResult exact = execution is null
            ? new ProbabilityCalculation(cards, input.HandSize, budget).CalculateExactResults(combos, input.Groups)
            : execution.Calculate(combos, input.Groups);
        string outcome = "Success";
        ProbabilityCalculationResult? result = null;
#if MODE_BASELINE
        result = exact.ToPublic(combos, input.Groups, budget);
        if (input.Options?.CertifySearchAvailability == true) {
            result = result with { SearchAvailability = SearchAvailabilityCertification.TryCalculate(combos, input.Groups, exact, budget, execution) };
        }
#else
        try {
            result = ProbabilityCalculatorService.CompleteResult(combos, input.Groups, input.Options?.Mode ?? CalculationModel.Fast,
                exact, budget, execution);
        }
        catch (UnsupportedExactCalculationException) {
            outcome = "Unsupported";
        }
#endif
        double milliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        // Frozen internal fractions also cover the deliberately withheld primary
        // for Unsupported. They are benchmark diagnostics, never UI results.
        return JsonSerializer.Serialize(new ModeMeasurement(milliseconds, allocated, outcome, budget.Spent, budget.PeakEntries, budget.PeakCells,
            exact.Total.ToFractionForBenchmark(), exact.Combos.Select(ToFractionForBenchmark).ToArray(), exact.Groups.Select(ToFractionForBenchmark).ToArray(), result),
            ModeMeasurementJsonContext.Default.ModeMeasurement);
    }

    private static string ToFractionForBenchmark(this ExactProbability value) => $"{value.Numerator}/{value.Denominator}";
}

internal sealed record ModeMeasurement(double Milliseconds, long Allocated, string Outcome, long Work, long Entries, long Cells,
    string Total, string[] Combos, string[] Groups, ProbabilityCalculationResult? Result);

[JsonSerializable(typeof(ModeMeasurement))]
internal partial class ModeMeasurementJsonContext : JsonSerializerContext;
