namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Certification;

internal enum SolverRoute { Ordinary, CountState, Exchangeable, Ordered, OrderedDeferred, SearchOnly, SearchCountState, SearchOrdered, SearchOrderedDeferred, SearchPhysicalCountState, SearchPhysicalEnumeration, SearchPhysicalFrontier, SearchPhysicalDeferred, UnifiedActivation }
internal enum PhysicalSearchAccuracy { ProvenExact, OptimisticSurvivalUnproven }
internal enum CapabilityReason { NoActiveSearch, NoReachableSearchTarget, ZeroDeckCapacity, NoReachableSearchTargetLoss, ConsumedSearchTarget, HandCostMayRemoveTarget, HandRemovalMayRemoveTarget, HiddenDeckLossMayRemoveTarget, ConditionalPhysicalSurvivors, ExplicitPhysicalSurvivors, ConditionalSelectiveSurvivors, CausalSearchDecisions }
internal enum CertificationSupport { ReusePrimary, ElementaryCountStateLowerPass, OrderedTerminalLowerPass, Unsupported }

// A value describing one COMPLETED execution, never a preflight risk estimate.
// The reference keeps saved plans, activation order and removal policy fixed.
// Search decisions optimize each result objective using current availability,
// never future draws. Legacy survival certificates require a phase-equivalence
// proof before dispatch; they do not certify a different activation policy.
internal readonly record struct CalculationCapability(
    SolverRoute Route,
    PhysicalSearchAccuracy Accuracy,
    CapabilityReason Reason,
    CertificationSupport Certification
) {
    internal const string Reference = "Configured activation/removal policy with causal Deck-search choices and physical target availability (v2)";

    internal string Explanation => Explain(Reason);

    internal static string Explain(CapabilityReason reason) => reason switch {
        CapabilityReason.CausalSearchDecisions => "Physical chronological resolution with optimal search choices from current information, without future draw knowledge.",
        CapabilityReason.ExplicitPhysicalSurvivors => "Explicit ordered identities with joint conditional hidden losses under the fixed policy.",
        CapabilityReason.ConditionalPhysicalSurvivors => "Joint physical Deck-search survival counted conditionally on the proven draw count state.",
        CapabilityReason.ConditionalSelectiveSurvivors => "Joint physical Deck-search survival conditioned on observed sources, hand payments and exchangeable unseen copies.",
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

    internal static bool ProvesSearchSurvival(CapabilityReason reason) =>
        reason is CapabilityReason.NoReachableSearchTarget or CapabilityReason.ZeroDeckCapacity or CapabilityReason.NoReachableSearchTargetLoss;

    internal static CalculationCapability Search(SolverRoute route, CapabilityReason reason) {
        bool exact = ProvesSearchSurvival(reason);
        CertificationSupport support = exact ? CertificationSupport.ReusePrimary
            : route == SolverRoute.SearchCountState ? CertificationSupport.ElementaryCountStateLowerPass
            : route is SolverRoute.SearchOrdered or SolverRoute.SearchOrderedDeferred ? CertificationSupport.OrderedTerminalLowerPass
            : CertificationSupport.Unsupported;
        return new(route, exact ? PhysicalSearchAccuracy.ProvenExact : PhysicalSearchAccuracy.OptimisticSurvivalUnproven, reason, support);
    }
}
