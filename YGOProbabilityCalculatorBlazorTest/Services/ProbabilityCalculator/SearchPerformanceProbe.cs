using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

// Untimed preparation; the delegate measures only one exact engine request.
public static class SearchPerformanceProbe {
    public static Func<string> Prepare(string json) => PrepareCore(json, false);

    public static Func<string> PrepareDiagnostic(string json) => PrepareCore(json, true);

    private static Func<string> PrepareCore(string json, bool diagnostics) {
        CalculationInput input = JsonSerializer.Deserialize(json, CalculationJsonContext.Default.CalculationInput)!;
        // Untimed reflection lets this identical probe compile on pre-search dev:
        // materialize whichever immutable card constructor that build exposes.
        ConstructorInfo constructor = typeof(Card).GetConstructors().Single();
        ParameterInfo[] parameters = constructor.GetParameters();
        List<Card> cards = input.Cards.Select(card => (Card)constructor.Invoke(parameters.Select(parameter => {
            PropertyInfo? property = typeof(WorkerCard).GetProperty(parameter.Name!,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            return property is null ? parameter.DefaultValue : property.GetValue(card);
        }).ToArray())).ToList();
        List<Combo> combos = input.Combos.Select(combo => new Combo(combo.Categories, combo.Name, true, combo.GroupId, combo.Cards, combo.AlternativeGroups)).ToList();
        return () => {
            SearchCountingDiagnostics? probe = diagnostics ? new() : null;
            WorkBudget budget = new(CalculationWorkPolicy.Interactive, CalculationStoragePolicy.Standard) { SearchDiagnostics = probe };
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            ExactCalculationResult? result = null;
            string outcome = "Success";
            SearchAvailabilityCertificate? certificate = null;
            try {
                result = new DrawEffectCalculation(cards, input.HandSize, budget, input.Options?.DrawActivationPriority)
                    .Calculate(combos, input.Groups);
                if (input.Options?.CertifySearchAvailability == true) {
                    certificate = SearchAvailabilityCertification.TryCalculate(cards, input.HandSize, combos, input.Groups, result, budget);
                }
            }
            catch (ProbabilityCalculationLimitException ex) {
                outcome = ex.Reason.ToString();
            }
            double milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            string Ratio(ExactProbability value) => $"{value.Numerator}/{value.Denominator}";
            return JsonSerializer.Serialize(new { Milliseconds = milliseconds, Allocated = allocated, Outcome = outcome, Certificate = certificate,
                Diagnostics = probe is null ? null : new { probe.Counts, probe.Milliseconds },
                Work = budget.Spent, Entries = budget.PeakEntries, Cells = budget.PeakCells, Total = result is null ? null : Ratio(result.Total),
                Combos = result?.Combos.Select(Ratio).ToArray(), Groups = result?.Groups.Select(Ratio).ToArray() });
        };
    }
}
