using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;

[SupportedOSPlatform("browser")]
public static partial class CalculationWorkerExports {
    [JSExport]
    public static string Calculate(string json) => CalculationWire.Execute(json);
}
