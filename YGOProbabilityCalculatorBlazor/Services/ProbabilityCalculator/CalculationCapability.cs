namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

internal enum SolverRoute { Ordinary, CountState, Exchangeable, Ordered, OrderedDeferred, SearchOnly, SearchCountState, SearchOrdered, SearchOrderedDeferred }
internal enum PhysicalSearchAccuracy { ProvenExact, OptimisticSurvivalUnproven }
internal enum CapabilityReason { NoActiveSearch, NoReachableSearchTarget, ZeroDeckCapacity, NoReachableSearchTargetLoss, ConsumedSearchTarget, HandCostMayRemoveTarget, HandRemovalMayRemoveTarget, HiddenDeckLossMayRemoveTarget }
internal enum CertificationSupport { ReusePrimary, ElementaryCountStateLowerPass, Unsupported }

// A value describing one COMPLETED execution, never a preflight risk estimate.
// The reference keeps the saved plans, materialized memberships, activation and
// removal/searcher-retention policy fixed; only final search survivor capacity
// differs. It makes no claim about optimal play or unmodeled game rules.
internal readonly record struct CalculationCapability(
    SolverRoute Route,
    PhysicalSearchAccuracy Accuracy,
    CapabilityReason Reason,
    CertificationSupport Certification
) {
    internal const string Reference = "Deck-search target survival under fixed modeled draw/removal policy (v1)";

    internal string Explanation => Reason switch {
        CapabilityReason.NoActiveSearch => "No active Deck searches in the executed model.",
        CapabilityReason.NoReachableSearchTarget => "No active search has an eligible target.",
        CapabilityReason.ZeroDeckCapacity => "The Deck has no remaining capacity for searches.",
        CapabilityReason.NoReachableSearchTargetLoss => "No eligible search target can leave hand or Deck before searching.",
        CapabilityReason.ConsumedSearchTarget => "A consumed draw source is eligible for a search.",
        CapabilityReason.HandCostMayRemoveTarget => "A hand cost may remove an eligible search target.",
        CapabilityReason.HandRemovalMayRemoveTarget => "A mandatory hand removal may remove an eligible search target.",
        CapabilityReason.HiddenDeckLossMayRemoveTarget => "Hidden Deck removal may remove an eligible search target.",
        _ => throw new InvalidOperationException("Unknown capability reason.")
    };

    internal static CalculationCapability NoSearch(SolverRoute route) =>
        new(route, PhysicalSearchAccuracy.ProvenExact, CapabilityReason.NoActiveSearch, CertificationSupport.ReusePrimary);

    internal static CalculationCapability Search(SolverRoute route, CapabilityReason reason) {
        bool exact = reason is CapabilityReason.NoReachableSearchTarget or CapabilityReason.ZeroDeckCapacity
            or CapabilityReason.NoReachableSearchTargetLoss;
        CertificationSupport support = exact ? CertificationSupport.ReusePrimary
            : route == SolverRoute.SearchCountState ? CertificationSupport.ElementaryCountStateLowerPass
            : CertificationSupport.Unsupported;
        return new(route, exact ? PhysicalSearchAccuracy.ProvenExact : PhysicalSearchAccuracy.OptimisticSurvivalUnproven, reason, support);
    }
}
