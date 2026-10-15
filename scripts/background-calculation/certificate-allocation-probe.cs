// Measurement-only source, excluded from the application build. Copy into an
// isolated app checkout for optimized publishing, then remove it. Also add an
// internal static WorkBudget? MeasurementBudget field and assign it to this at
// the start of WorkBudget.Spend. Never use the instrumented build for timings.
using System.Globalization;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;

[SupportedOSPlatform("browser")]
public static partial class CertificateAllocationProbe {
    [JSExport]
    public static string Measure(string json) {
        WorkBudget.MeasurementBudget = null;
        long start = GC.GetAllocatedBytesForCurrentThread();
        string response = CalculationWire.Execute(json);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - start;
        WorkBudget budget = WorkBudget.MeasurementBudget!;
        return string.Join("\n", bytes.ToString(CultureInfo.InvariantCulture), budget.Spent.ToString(CultureInfo.InvariantCulture),
            budget.PeakEntries.ToString(CultureInfo.InvariantCulture), budget.PeakCells.ToString(CultureInfo.InvariantCulture), response);
    }
}
