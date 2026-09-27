namespace YGOProbabilityCalculatorBlazor.Services.Interface;

/// <summary>The request exceeded the engine's bounded work or storage budget; no result was produced.</summary>
public sealed class ProbabilityCalculationLimitException() : Exception(
    "Calculation stopped because it exceeded the work or memory limit. Simplify the combos or category constraints and try again.");
