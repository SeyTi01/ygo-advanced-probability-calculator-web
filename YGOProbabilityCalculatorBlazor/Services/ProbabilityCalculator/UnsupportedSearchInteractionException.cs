namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

public sealed class UnsupportedSearchInteractionException() : ArgumentException(
    "Search effects cannot be combined with hidden deck banishes or mills: remaining target identities are unknown. Disable those operations or choose Opening hand only.");
