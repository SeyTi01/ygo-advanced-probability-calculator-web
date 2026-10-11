namespace YGOProbabilityCalculatorBlazor.Services.Interface;

// Independent of stored card settings; suitable for both direct and worker calls.
public sealed record CalculationOptions(bool ConditionalEffectsEnabled = false);
