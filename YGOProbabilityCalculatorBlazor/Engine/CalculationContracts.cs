using System.Collections.Immutable;

namespace CardProbability;

/// <summary>Selects the primary calculation model; mode alone does not establish a result's accuracy.</summary>
public enum CalculationMode {
    /// <summary>Count only the dealt hand. Supplied effect plans are still validated before execution is suppressed.</summary>
    OpeningHand,
    /// <summary>Use faster counting routes; a completed result may be exact, bounded or uncertified.</summary>
    Fast,
    /// <summary>Require physical-exact counting within the supported fixed policy, or return a failure without rows.</summary>
    Exact,
    /// <summary>Explicit compatibility mode for the historical optimistic search-access projection.</summary>
    OptimisticSearchProjection
}

/// <summary>The terminal outcome of a synchronous call; only Completed supplies probability rows.</summary>
public enum CalculationStatus {
    /// <summary>The primary calculation completed. Inspect accuracy and certification separately.</summary>
    Completed,
    /// <summary>Cooperative cancellation was observed.</summary>
    Cancelled,
    /// <summary>Inputs contain invalid IDs, references, counts, options or bounds.</summary>
    InvalidInput,
    /// <summary>The requested operation composition or solver capability is unsupported.</summary>
    Unsupported,
    /// <summary>The work allowance was exhausted before primary completion.</summary>
    WorkLimit,
    /// <summary>A per-structure storage limit was exceeded before primary completion.</summary>
    StorageLimit
}

/// <summary>Strength of the physical probability proof, independently of the exact primary rational.</summary>
public enum ProbabilityAccuracy {
    /// <summary>The physical probability is proven; certified endpoints coincide.</summary>
    Exact,
    /// <summary>A certified interval encloses the physical probability.</summary>
    Bounded,
    /// <summary>No usable physical interval was established; the primary can be a projection.</summary>
    Uncertified
}

/// <summary>The rules whose probability is being evaluated.</summary>
public enum ModelScope {
    /// <summary>Only the dealt hand, without effect execution.</summary>
    OpeningHand,
    /// <summary>Automatic draws, at most one excavation, then optional searches under the fixed policy.</summary>
    FixedOrderEffects
}

/// <summary>Ordering of eligible sources within the draw and search phases; never reorders the phases.</summary>
public enum ActivationOrder {
    /// <summary>Use physical encounter order.</summary>
    EncounterOrder,
    /// <summary>Rank listed entries first, retaining encounter order for ties and unlisted entries.</summary>
    Priority
}

/// <summary>Outcome of optional search-availability certification, separate from primary completion.</summary>
public enum CertificationStatus {
    /// <summary>No optional certificate was needed. On completed results the primary has an exact proof.</summary>
    NotRequired,
    /// <summary>Optional certification established the exact physical probability.</summary>
    Exact,
    /// <summary>Optional certification established physical lower and upper bounds.</summary>
    Bounded,
    /// <summary>No active deck searches required this certification in the executed model.</summary>
    NotApplicable,
    /// <summary>No usable optional certificate was established, including when it was omitted.</summary>
    NotCertified,
    /// <summary>Optional certification reached a resource limit after primary completion.</summary>
    Limited,
    /// <summary>Optional certification encountered an error after primary completion.</summary>
    Error
}

/// <summary>Finite work is optional. Storage limits apply per live structure, not to total process memory.</summary>
/// <param name="WorkUnits">Positive deterministic operation allowance, or null for unlimited work; never elapsed time.</param>
/// <param name="EntryLimit">Positive maximum retained entries per structure.</param>
/// <param name="CellLimit">Positive maximum 32-bit cells per structure, including retained integer payload.</param>
public sealed record ExecutionPolicy(long? WorkUnits = null, long EntryLimit = 32_768, long CellLimit = 262_144);

/// <summary>Immutable execution settings; priority input is copied at construction.</summary>
public sealed class CalculationOptions {
    /// <summary>The primary model to execute.</summary>
    public CalculationMode Mode { get; }
    /// <summary>Request-local work and per-structure storage limits.</summary>
    public ExecutionPolicy Policy { get; }
    /// <summary>Source order within each draw/search phase.</summary>
    public ActivationOrder ActivationOrder { get; }
    /// <summary>Unique declared entry IDs, highest priority first; ignored for ordering in EncounterOrder mode.</summary>
    public ImmutableArray<string> PriorityEntryIds { get; }

    /// <summary>Constructs options; validation occurs when calculating.</summary>
    /// <param name="mode">Exact by default.</param>
    /// <param name="policy">Null uses 150,000,000 work units for Fast, unlimited work otherwise, with default storage limits.</param>
    /// <param name="activationOrder">Encounter order by default; priority does not affect excavation selection.</param>
    /// <param name="priorityEntryIds">Optional ordered subset of entry IDs. References are validated in every mode.</param>
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
/// <param name="Numerator">Base-ten integer numerator.</param>
/// <param name="Denominator">Positive base-ten integer denominator.</param>
public sealed record RationalProbability(string Numerator, string Denominator);

/// <summary>Proven physical probability interval; equal endpoints represent exact proof, not statistical confidence.</summary>
/// <param name="Lower">Inclusive lower bound.</param>
/// <param name="Upper">Inclusive upper bound.</param>
public sealed record CertifiedInterval(RationalProbability Lower, RationalProbability Upper);

/// <summary>Primary may describe a projection. Only Accuracy/Certified identify a proven physical probability.</summary>
/// <param name="Probability">Convenience double for Primary, not the certified midpoint.</param>
/// <param name="Primary">Exact rational for the primary model; a Fast projection may lie outside Certified.</param>
/// <param name="Accuracy">Strength of this row's physical proof.</param>
/// <param name="Certified">Physical bounds, or null when uncertified. Equal endpoints need not equal a projected Primary.</param>
public sealed record ProbabilityValue(
    double Probability,
    RationalProbability Primary,
    ProbabilityAccuracy Accuracy,
    CertifiedInterval? Certified
);

/// <summary>One goal's probability under the shared goal-dependent keep/removal policy.</summary>
/// <param name="Id">The request's goal ID.</param>
/// <param name="Value">Probability and proof for this goal; optional search choices may differ between rows.</param>
public sealed record GoalProbability(string Id, ProbabilityValue Value);

/// <summary>Overlap-safe union of the goals assigned to one reporting group.</summary>
/// <param name="Id">The request's group ID.</param>
/// <param name="GoalCount">Number of goals contributing to this group.</param>
/// <param name="Value">Probability of at least one contributing goal, not a sum of their probabilities.</param>
public sealed record GroupProbability(string Id, int GoalCount, ProbabilityValue Value);

/// <summary>Deterministic accounting, not elapsed time or total process memory measurements.</summary>
/// <param name="WorkSpent">Charged operation units, saturating at Int64.MaxValue.</param>
/// <param name="RemainingWork">Remaining finite allowance, or null for unlimited work.</param>
/// <param name="PeakEntries">Largest observed entry count of an individual checked structure.</param>
/// <param name="PeakCells">Largest observed 32-bit cell count of an individual checked structure.</param>
public sealed record CalculationMetrics(long WorkSpent, long? RemainingWork, long PeakEntries, long PeakCells);

/// <summary>A synchronous terminal outcome. Failure outcomes contain no probability rows.</summary>
/// <param name="Status">Primary completion or failure status.</param>
/// <param name="Mode">Requested calculation mode.</param>
/// <param name="Scope">Model rules applied by this call.</param>
/// <param name="Detail">Human-readable explanation; not a stable machine-readable error code.</param>
/// <param name="Accuracy">Overall physical accuracy on completion, otherwise null; also inspect each row.</param>
/// <param name="Total">Union of all goals on completion, otherwise null.</param>
/// <param name="Goals">Individual goal rows in request order; empty on failure or for CalculateUnion.</param>
/// <param name="Groups">Reporting group unions; empty on failure or for CalculateUnion.</param>
/// <param name="Metrics">Accounting accumulated by this call, including before a failure.</param>
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
    /// <summary>Runtime engine model semantics version, independent of package, session, effect-plan and wire versions.</summary>
    /// <remarks>Read from the returned instance so a compiled consumer obtains the running engine's version.</remarks>
    public int ModelVersion => 1;

    /// <summary>Always true; this API never returns preliminary or in-progress results.</summary>
    public bool IsTerminal => true;

    /// <summary>Optional certificate outcome. Consult Status first; this does not turn a valid primary into a failure.</summary>
    public CertificationStatus Certification { get; init; } = CertificationStatus.NotRequired;

    /// <summary>Optional human-readable certificate explanation, independent of the primary Detail.</summary>
    public string? CertificationDetail { get; init; }
}

/// <summary>Conservative structural guidance, not a work estimate or solver-routing contract.</summary>
/// <param name="ElevatedRisk">True when a warning was produced. False also covers invalid, unsupported or assessment-budget-limited input; it is no validity or completion guarantee.</param>
public sealed record CalculationAssessment(bool ElevatedRisk);
