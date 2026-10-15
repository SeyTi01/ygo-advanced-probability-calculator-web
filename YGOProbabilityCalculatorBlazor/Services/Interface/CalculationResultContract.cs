using System.Collections.Immutable;
using System.Text.Json.Serialization;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.Interface;

public enum CalculationCompletion { Success, Unsupported, WorkLimit, StorageLimit, InvalidInput, Error, Cancelled }
public enum CalculationAccuracy { Exact, Bounded, Uncertified }
public enum CalculationRuleScope { OpeningHand, SavedEffectsFixedPolicy }

// All rows belong to one executed request and sample space. Decimal integer
// strings retain the engine's rational values through JavaScript and exports.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CalculationFractions(
    ProbabilityFraction Total,
    ImmutableArray<ProbabilityFraction> Combos,
    ImmutableArray<ProbabilityFraction> Groups
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CalculationResultContract(
    int Version,
    CalculationModel RequestedMode,
    CalculationModel ExecutedMode,
    string Reference,
    CalculationRuleScope Scope,
    CalculationCompletion Completion,
    CalculationAccuracy? Accuracy,
    string Reason,
    string? SolverRoute = null,
    string? Proof = null,
    CalculationFractions? Fractions = null
) {
    public const int CurrentVersion = 1;
    public const string OpeningReference = "Dealt opening hand satisfying the stated requirements; all effects ignored (v1)";
    public const string EffectsReference = ProbabilityCalculator.CalculationCapability.Reference;

    internal static CalculationResultContract Failure(CalculationModel mode, CalculationCompletion completion, string reason) =>
        new(
            CurrentVersion,
            mode,
            mode,
            mode == CalculationModel.OpeningHand ? OpeningReference : EffectsReference,
            mode == CalculationModel.OpeningHand ? CalculationRuleScope.OpeningHand : CalculationRuleScope.SavedEffectsFixedPolicy,
            completion,
            null,
            reason
        );
}

public sealed class UnsupportedExactCalculationException(CalculationResultContract contract)
    : Exception("Unsupported exact calculation: " + contract.Reason) {
    public CalculationResultContract Contract { get; } = contract;
}
