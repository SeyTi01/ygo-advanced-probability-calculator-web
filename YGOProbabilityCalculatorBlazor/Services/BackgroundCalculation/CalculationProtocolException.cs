namespace YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;

public enum CalculationProtocolFailureReason {
    MissingResponse,
    InvalidResponse,
    InvalidFailureContract,
    InvalidUnsupportedResponse,
    MissingResult,
    MissingAccuracyContract,
    InvalidAccuracyContract,
    InvalidRationalValue,
    InvalidAutomaticExactOutcome,
    ContradictoryProof,
    InvalidCertificate,
    IncompleteIntervals,
    InvalidInterval,
    UnexpectedIntervals,
    InvalidPreliminaryResult
}

// Produced only by the response boundary; a valid worker-reported error is not
// a protocol failure. Messages remain suitable for the existing UI error path.
public sealed class CalculationProtocolException : InvalidOperationException {
    public CalculationProtocolFailureReason Reason { get; }
    // Flattened result order: total, then combo rows, then group rows.
    public int? RowIndex { get; }

    private CalculationProtocolException(
        CalculationProtocolFailureReason reason,
        string message,
        int? rowIndex = null
    ) : base(message) {
        Reason = reason;
        RowIndex = rowIndex;
    }

    internal static CalculationProtocolException MissingResponse() =>
        new(CalculationProtocolFailureReason.MissingResponse, "Background calculation returned no response.");

    internal static CalculationProtocolException InvalidResponse() =>
        new(CalculationProtocolFailureReason.InvalidResponse, "Background calculation returned an invalid response.");

    internal static CalculationProtocolException InvalidFailureContract() =>
        new(CalculationProtocolFailureReason.InvalidFailureContract, "Invalid worker failure contract.");

    internal static CalculationProtocolException InvalidUnsupportedResponse() =>
        new(CalculationProtocolFailureReason.InvalidUnsupportedResponse, "Invalid unsupported calculation response.");

    internal static CalculationProtocolException MissingResult() =>
        new(CalculationProtocolFailureReason.MissingResult, "Background calculation returned no result.");

    internal static CalculationProtocolException MissingAccuracyContract() =>
        new(CalculationProtocolFailureReason.MissingAccuracyContract, "Worker result has no accuracy contract.");

    internal static CalculationProtocolException InvalidAccuracyContract() =>
        new(CalculationProtocolFailureReason.InvalidAccuracyContract, "Invalid worker accuracy contract or rows.");

    internal static CalculationProtocolException InvalidRationalValue(int rowIndex) =>
        new(CalculationProtocolFailureReason.InvalidRationalValue, "Invalid worker rational value.", rowIndex);

    internal static CalculationProtocolException InvalidAutomaticExactOutcome() =>
        new(CalculationProtocolFailureReason.InvalidAutomaticExactOutcome, "Invalid automatic Exact outcome.");

    internal static CalculationProtocolException ContradictoryProof() =>
        new(CalculationProtocolFailureReason.ContradictoryProof, "Worker accuracy contradicts its proof or certificate.");

    internal static CalculationProtocolException InvalidCertificate() =>
        new(CalculationProtocolFailureReason.InvalidCertificate, "Invalid worker certificate.");

    internal static CalculationProtocolException IncompleteIntervals() =>
        new(CalculationProtocolFailureReason.IncompleteIntervals, "Incomplete worker intervals.");

    internal static CalculationProtocolException InvalidInterval(int rowIndex) =>
        new(CalculationProtocolFailureReason.InvalidInterval, "Invalid worker interval.", rowIndex);

    internal static CalculationProtocolException UnexpectedIntervals() =>
        new(CalculationProtocolFailureReason.UnexpectedIntervals, "Unavailable certificate contains partial intervals.");

    internal static CalculationProtocolException InvalidPreliminaryResult() =>
        new(CalculationProtocolFailureReason.InvalidPreliminaryResult, "Invalid preliminary Fast result.");
}
