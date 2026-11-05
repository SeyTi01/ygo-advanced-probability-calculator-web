using System.Collections.Immutable;

namespace CardProbability;

public enum CalculationMode { OpeningHand, Fast, Exact, OptimisticSearchProjection }
public enum CalculationStatus { Completed, Cancelled, InvalidInput, Unsupported, WorkLimit, StorageLimit }
public enum ProbabilityAccuracy { Exact, Bounded, Uncertified }
public enum ModelScope { OpeningHand, FixedOrderEffects }
public enum ActivationOrder { EncounterOrder, Priority }
public enum CertificationStatus { NotRequired, Exact, Bounded, NotApplicable, NotCertified, Limited, Error }

/// <summary>Finite work is optional. Storage limits apply per live structure, not to total process memory.</summary>
public sealed record ExecutionPolicy(long? WorkUnits = null, long EntryLimit = 32_768, long CellLimit = 262_144);

public sealed class CalculationOptions {
    public CalculationMode Mode { get; }
    public ExecutionPolicy Policy { get; }
    public ActivationOrder ActivationOrder { get; }
    public ImmutableArray<string> PriorityEntryIds { get; }

    public CalculationOptions(
        CalculationMode mode = CalculationMode.Exact,
        ExecutionPolicy? policy = null,
        ActivationOrder activationOrder = ActivationOrder.EncounterOrder,
        IEnumerable<string>? priorityEntryIds = null
    ) {
        Mode = mode;
        Policy = policy ?? new(mode == CalculationMode.Fast ? 150_000_000 : null);
        ActivationOrder = activationOrder;
        PriorityEntryIds = priorityEntryIds?.ToImmutableArray() ?? [];
    }
}

/// <summary>Exact rational value in lowest terms; strings preserve integers across host transports.</summary>
public sealed record RationalProbability(string Numerator, string Denominator);
public sealed record CertifiedInterval(RationalProbability Lower, RationalProbability Upper);

/// <summary>Primary may describe a projection. Only Accuracy/Certified identify a proven physical probability.</summary>
public sealed record ProbabilityValue(
    double Probability,
    RationalProbability Primary,
    ProbabilityAccuracy Accuracy,
    CertifiedInterval? Certified
);

public sealed record GoalProbability(string Id, ProbabilityValue Value);
public sealed record GroupProbability(string Id, int GoalCount, ProbabilityValue Value);
public sealed record CalculationMetrics(long WorkSpent, long? RemainingWork, long PeakEntries, long PeakCells);

/// <summary>A synchronous terminal outcome. Failure outcomes contain no probability rows.</summary>
public sealed record CalculationResult(
    CalculationStatus Status,
    CalculationMode Mode,
    ModelScope Scope,
    string Detail,
    ProbabilityAccuracy? Accuracy,
    ProbabilityValue? Total,
    ImmutableArray<GoalProbability> Goals,
    ImmutableArray<GroupProbability> Groups,
    CalculationMetrics Metrics
) {
    public const int ModelVersion = 1;
    public bool IsTerminal => true;
    public CertificationStatus Certification { get; init; } = CertificationStatus.NotRequired;
    public string? CertificationDetail { get; init; }
}

/// <summary>Conservative structural guidance, not a work estimate or solver-routing contract.</summary>
public sealed record CalculationAssessment(bool ElevatedRisk);
