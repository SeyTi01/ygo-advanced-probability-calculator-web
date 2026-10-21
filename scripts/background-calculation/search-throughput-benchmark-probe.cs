// Measurement-only external Compile include for both frozen base and candidate.
// Never include this probe in a production publish. MeasureCertificate=false
// isolates primary Fast work; true uses the real public completion path.
// Define ADAPTIVE_FAST for both post-221 measurement builds.
// UnlimitedExact=true explicitly overrides finite Input.WorkUnits for Exact only.
// Omitted/false preserves the stored finite allowance and bounded wire default.
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
            card.FirstActivationOnly, card.BlocksLaterDraws, card.Search, card.OncePerTurn, card.ActivationLimit, card.MinimumHandSize)).ToList();
        List<Combo> combos = input.Combos.Select(combo => new Combo(combo.Categories, combo.Name, true,
            combo.GroupId, combo.Cards, combo.AlternativeGroups)).ToList();
        bool diagnose = document.RootElement.TryGetProperty("Diagnose", out JsonElement diag) && diag.GetBoolean();
        SearchCountingDiagnostics? diagnostics = diagnose ? new() : null;
        bool unlimitedExact = document.RootElement.TryGetProperty("UnlimitedExact", out JsonElement unlimited) && unlimited.GetBoolean();
        if (unlimitedExact && input.Options?.Mode != CalculationModel.Exact) {
            throw new ArgumentException("UnlimitedExact requires Input.Options.Mode to be Exact.", nameof(json));
        }
        CalculationWorkPolicy workPolicy = unlimitedExact
            ? CalculationWorkPolicy.Unlimited
            : new(input.WorkUnits ?? CalculationWorkPolicy.Default.WorkUnits);
        WorkBudget budget = new(workPolicy,
            CalculationStoragePolicy.ForTier(input.StorageTier ?? CalculationStorageTier.Standard)) { SearchDiagnostics = diagnostics };
        string policyName = workPolicy.IsUnlimited ? "Unlimited" : "Finite";
        long? workLimit = workPolicy.IsUnlimited ? null : workPolicy.WorkUnits;
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
            diagnostics?.Counts, diagnostics?.Milliseconds, GC.GetTotalMemory(false), OperatingSystem.IsBrowser() ? null : Process.GetCurrentProcess().WorkingSet64,
            exact?.Total.ToFraction(), exact?.Combos.Select(value => value.ToFraction()).ToArray(), exact?.Groups.Select(value => value.ToFraction()).ToArray(), result, policyName, workLimit),
            FastCertificateMeasurementJsonContext.Default.FastCertificateMeasurement);
    }
}

internal sealed record FastCertificateMeasurement(double Milliseconds, long Allocated, string Outcome, string? Route, long Work, long Entries, long Cells,
    Dictionary<string, long>? Counts, Dictionary<string, double>? Phases, long ManagedHeapBytes, long? WorkingSetBytes,
    ProbabilityFraction? Total, ProbabilityFraction[]? Combos, ProbabilityFraction[]? Groups, ProbabilityCalculationResult? Result,
    string WorkPolicy, long? WorkLimit);

[JsonSerializable(typeof(FastCertificateMeasurement))]
internal partial class FastCertificateMeasurementJsonContext : JsonSerializerContext;
