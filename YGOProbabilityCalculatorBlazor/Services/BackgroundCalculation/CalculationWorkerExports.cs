using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;

[SupportedOSPlatform("browser")]
public static partial class CalculationWorkerExports {
    [JSExport]
    public static string Calculate(string json) => CalculationWire.Execute(json);

    [JSExport]
    public static string CalculateInteractiveExact(string json) => CalculationWire.ExecuteInteractiveExact(json);

    [JSExport]
    public static string UpgradeProgressiveFast(string json) => CalculationWire.ExecuteProgressiveUpgrade(json);

    [JSExport]
    public static string BeginFast(string json) => CalculationWire.ExecuteFastStage(json);

    [JSExport]
    public static string UpgradeFast(string json, string remainingWork) => CalculationWire.ExecuteFastStage(json, remainingWork);
}
