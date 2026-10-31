using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace YGOProbabilityCalculatorBlazor.Services.Interface;

public enum SearchAvailabilityStatus { Exact, Bounded, NotApplicable, NotCertified, Limited, Error }

public enum SearchAvailabilityRowStatus { Exact, Informative, Uninformative }

// Decimal strings preserve arbitrarily large integers through worker JSON/JavaScript.
// These are probabilities, not percentages; the denominator is strictly positive.
public sealed record ProbabilityFraction(string Numerator, string Denominator);

public sealed record SearchAvailabilityInterval(string Label, ProbabilityFraction Lower, ProbabilityFraction Upper) {
    [JsonIgnore] public bool IsExact => Lower == Upper;
    [JsonIgnore] public SearchAvailabilityRowStatus Informativeness => IsExact
        ? SearchAvailabilityRowStatus.Exact
        : Lower.Numerator == "0"
            ? SearchAvailabilityRowStatus.Uninformative
            : SearchAvailabilityRowStatus.Informative;
}

// Owned by the same accepted immutable worker result as the primary probabilities.
// No certificate is stored in sessions or applied to later workspace inputs.
public sealed record SearchAvailabilityCertificate(
    SearchAvailabilityStatus Status,
    string Detail,
    SearchAvailabilityInterval? Total = null,
    ImmutableArray<SearchAvailabilityInterval> Combos = default,
    ImmutableArray<SearchAvailabilityInterval> Groups = default
) {
    public string Reference => ProbabilityCalculator.Certification.CalculationCapability.Reference;

    internal static SearchAvailabilityCertificate Unavailable(SearchAvailabilityStatus status, string detail) =>
        new(status, detail, null, [], []);
}
