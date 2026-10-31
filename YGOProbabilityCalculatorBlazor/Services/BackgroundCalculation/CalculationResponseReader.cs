using System.Globalization;
using System.Numerics;
using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;

// Accepts only complete version-local worker responses. Expected calculation
// failures retain their own contracts; malformed protocol data is rejected here.
internal static class CalculationResponseReader {
    public static ProbabilityCalculationResult Read(string json, CalculationModel? requestedMode = null) {
        CalculationResponse response = JsonSerializer.Deserialize(json, CalculationJsonContext.Default.CalculationResponse) ?? throw CalculationProtocolException.MissingResponse();

        bool hasInvalidResponse = response.Result is { ComboProbabilities: null }
            || (response.LimitReason is { } reason && !Enum.IsDefined(reason))
            || (response.FailureKind is { } kind && !Enum.IsDefined(kind))
            || (response.LimitReason is not null && response.FailureKind is not null)
            || (response.Result is not null && response.Contract is not null)
            || (response.Result is not null && (response.Error is not null || response.LimitReason is not null || response.FailureKind is not null))
            || (response.Error is null && (response.LimitReason is not null || response.FailureKind is not null));

        if (hasInvalidResponse) {
            throw CalculationProtocolException.InvalidResponse();
        }

        if (response.Contract is { } failure) {
            CalculationCompletion expected = response.LimitReason switch {
                ProbabilityCalculationLimitReason.Work => CalculationCompletion.WorkLimit,
                ProbabilityCalculationLimitReason.Storage => CalculationCompletion.StorageLimit,

                _ => response.FailureKind switch {
                    CalculationFailureKind.Unsupported => CalculationCompletion.Unsupported,
                    CalculationFailureKind.Cancelled => CalculationCompletion.Cancelled,
                    CalculationFailureKind.Input => CalculationCompletion.InvalidInput,
                    _ => CalculationCompletion.Error
                }
            };

            if (!ValidHeader(failure, requestedMode) || failure.Completion != expected
                || failure.Accuracy is not null || failure.Fractions is not null || failure.AutomaticExact is not null) {
                throw CalculationProtocolException.InvalidFailureContract();
            }
        }

        if (response.LimitReason is { } limit) {
            throw new ProbabilityCalculationLimitException(limit);
        }

        if (response.FailureKind == CalculationFailureKind.Input) {
            throw new ArgumentException(response.Error);
        }

        if (response.FailureKind == CalculationFailureKind.Unsupported) {
            if (response.Contract is not { Completion: CalculationCompletion.Unsupported, RequestedMode: CalculationModel.Exact, Accuracy: null, Fractions: null } contract) {
                throw CalculationProtocolException.InvalidUnsupportedResponse();
            }

            throw new UnsupportedExactCalculationException(contract);
        }

        if (response.FailureKind == CalculationFailureKind.Cancelled) {
            throw new OperationCanceledException(response.Error);
        }

        if (response.Error is not null) {
            throw new InvalidOperationException(response.Error);
        }

        ProbabilityCalculationResult result = response.Result ?? throw CalculationProtocolException.MissingResult();
        ValidateResult(result, requestedMode);

        return result;
    }

    private static void ValidateResult(ProbabilityCalculationResult result, CalculationModel? requestedMode) {
        // Legacy native result constructors remain available; version-local worker
        // success always carries the complete immutable contract.
        CalculationResultContract contract = result.Contract ?? throw CalculationProtocolException.MissingAccuracyContract();

        bool valid = ValidHeader(contract, requestedMode)
            && contract.Completion == CalculationCompletion.Success && contract.Accuracy is { } accuracy && Enum.IsDefined(accuracy)
            && Enum.IsDefined(contract.Scope) && !string.IsNullOrWhiteSpace(contract.Reason)
            && contract.Fractions is { Combos.IsDefault: false, Groups.IsDefault: false } fractions
            && fractions.Combos.Length == result.ComboProbabilities.Count
            && fractions.Groups.Length == (result.GroupProbabilities?.Count ?? 0);

        if (!valid || !ValidProbability(result.TotalProbability)
            || result.ComboProbabilities.Any(row => row is null || !ValidProbability(row.Probability))
            || result.GroupProbabilities?.Any(row => row is null || !ValidProbability(row.Probability)) == true) {
            throw CalculationProtocolException.InvalidAccuracyContract();
        }

        int rowIndex = 0;

        foreach (ProbabilityFraction fraction in new[] { contract.Fractions!.Total }.Concat(contract.Fractions.Combos).Concat(contract.Fractions.Groups)) {
            if (!ValidFraction(fraction)) {
                throw CalculationProtocolException.InvalidRationalValue(rowIndex);
            }

            rowIndex++;
        }

        if (contract.AutomaticExact is { } automatic && (!Enum.IsDefined(automatic)
            || contract.ExecutedMode != CalculationModel.Fast
            || automatic == AutomaticExactOutcome.Completed && contract.Accuracy != CalculationAccuracy.Exact)) {
            throw CalculationProtocolException.InvalidAutomaticExactOutcome();
        }

        SearchAvailabilityCertificate? certificate = result.SearchAvailability;

        if (contract.ExecutedMode != CalculationModel.Fast && (contract.Accuracy != CalculationAccuracy.Exact || certificate is not null)
            || contract.Accuracy == CalculationAccuracy.Exact && contract.Proof is null && certificate?.Status != SearchAvailabilityStatus.Exact
            || contract.Accuracy == CalculationAccuracy.Uncertified && certificate?.Status is not (SearchAvailabilityStatus.NotCertified or SearchAvailabilityStatus.Limited or SearchAvailabilityStatus.Error)
            || contract.Accuracy == CalculationAccuracy.Bounded && certificate?.Status != SearchAvailabilityStatus.Bounded) {
            throw CalculationProtocolException.ContradictoryProof();
        }

        if (certificate is not null) {
            if (!Enum.IsDefined(certificate.Status) || certificate.Combos.IsDefault || certificate.Groups.IsDefault) {
                throw CalculationProtocolException.InvalidCertificate();
            }

            bool hasIntervals = certificate.Status is SearchAvailabilityStatus.Bounded or SearchAvailabilityStatus.Exact or SearchAvailabilityStatus.NotApplicable;

            if (hasIntervals) {
                if (certificate.Total is null || certificate.Combos.Length != result.ComboProbabilities.Count
                    || certificate.Groups.Length != (result.GroupProbabilities?.Count ?? 0)) {
                    throw CalculationProtocolException.IncompleteIntervals();
                }

                SearchAvailabilityInterval[] rows = [certificate.Total, .. certificate.Combos, .. certificate.Groups];
                ProbabilityFraction[] primary = [contract.Fractions.Total, .. contract.Fractions.Combos, .. contract.Fractions.Groups];

                for (int index = 0; index < rows.Length; index++) {
                    SearchAvailabilityInterval row = rows[index];
                    // A completed necessary-event proof can reduce U below the
                    // unchanged optimistic primary. Still require L <= U <= primary.
                    if (row is null || !ValidFraction(row.Lower) || !ValidFraction(row.Upper)
                        || BigInteger.Parse(row.Upper.Numerator, CultureInfo.InvariantCulture) * BigInteger.Parse(primary[index].Denominator, CultureInfo.InvariantCulture)
                            > BigInteger.Parse(primary[index].Numerator, CultureInfo.InvariantCulture) * BigInteger.Parse(row.Upper.Denominator, CultureInfo.InvariantCulture)
                        || BigInteger.Parse(row.Lower.Numerator, CultureInfo.InvariantCulture) * BigInteger.Parse(row.Upper.Denominator, CultureInfo.InvariantCulture)
                            > BigInteger.Parse(row.Upper.Numerator, CultureInfo.InvariantCulture) * BigInteger.Parse(row.Lower.Denominator, CultureInfo.InvariantCulture)
                        || certificate.Status == SearchAvailabilityStatus.Exact && !row.IsExact) {
                        throw CalculationProtocolException.InvalidInterval(index);
                    }
                }
            }
            else if (certificate.Total is not null || !certificate.Combos.IsEmpty || !certificate.Groups.IsEmpty) {
                throw CalculationProtocolException.UnexpectedIntervals();
            }
        }
    }

    private static bool ValidProbability(double value) => double.IsFinite(value) && value is >= 0 and <= 1;

    private static bool ValidHeader(CalculationResultContract contract, CalculationModel? requestedMode) =>
        contract.Version == CalculationResultContract.CurrentVersion
        && Enum.IsDefined(contract.RequestedMode) && contract.RequestedMode == contract.ExecutedMode
        && (requestedMode is null || contract.RequestedMode == requestedMode)
        && Enum.IsDefined(contract.Completion)
        && contract.Reference == (contract.ExecutedMode == CalculationModel.OpeningHand
            ? CalculationResultContract.OpeningReference : CalculationResultContract.EffectsReference)
        && contract.Scope == (contract.ExecutedMode == CalculationModel.OpeningHand
            ? CalculationRuleScope.OpeningHand : CalculationRuleScope.SavedEffectsFixedPolicy);

    private static bool ValidFraction(ProbabilityFraction? value) => value is not null
        && BigInteger.TryParse(value.Numerator, NumberStyles.None, CultureInfo.InvariantCulture, out BigInteger numerator)
        && BigInteger.TryParse(value.Denominator, NumberStyles.None, CultureInfo.InvariantCulture, out BigInteger denominator)
        && denominator > 0 && numerator >= 0 && numerator <= denominator;

    public static void ValidatePreliminary(ProbabilityCalculationResult result) {
        if (result.Contract is not { Accuracy: not CalculationAccuracy.Exact, AutomaticExact: null }) {
            throw CalculationProtocolException.InvalidPreliminaryResult();
        }
    }
}
