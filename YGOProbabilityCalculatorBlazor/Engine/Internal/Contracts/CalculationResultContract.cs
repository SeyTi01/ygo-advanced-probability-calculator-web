using System.Collections.Immutable;
using System.Text.Json.Serialization;
using CardProbability.Internal.Model;

namespace CardProbability.Internal.Contracts;

internal enum CalculationCompletion { Success, Unsupported, WorkLimit, StorageLimit, InvalidInput, Error, Cancelled }
internal enum CalculationAccuracy { Exact, Bounded, Uncertified }
internal enum CalculationRuleScope { OpeningHand, SavedEffectsFixedPolicy }
// All rows belong to one executed request and sample space. Decimal integer
// strings retain arbitrarily large rational values at the result boundary.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record CalculationFractions(
    ProbabilityFraction Total,
    ImmutableArray<ProbabilityFraction> Combos,
    ImmutableArray<ProbabilityFraction> Groups
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record CalculationResultContract(
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
    public const string EffectsReference = CardProbability.Internal.Certification.CalculationCapability.Reference;

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

internal sealed class UnsupportedExactCalculationException(CalculationResultContract contract): Exception("Unsupported exact calculation: " + contract.Reason) {
    public CalculationResultContract Contract { get; } = contract;
}
