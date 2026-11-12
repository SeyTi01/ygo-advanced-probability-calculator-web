using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace CardProbability.Internal.Contracts;

internal enum SearchAvailabilityStatus { Exact, Bounded, NotApplicable, NotCertified, Limited, Error }

internal enum SearchAvailabilityRowStatus { Exact, Informative, Uninformative }

// Decimal strings preserve arbitrarily large integers at the result boundary.
// These are probabilities, not percentages; the denominator is strictly positive.
internal sealed record ProbabilityFraction(string Numerator, string Denominator);

internal sealed record SearchAvailabilityInterval(string Label, ProbabilityFraction Lower, ProbabilityFraction Upper) {
    [JsonIgnore] public bool IsExact => Lower == Upper;
    [JsonIgnore] public SearchAvailabilityRowStatus Informativeness => IsExact
        ? SearchAvailabilityRowStatus.Exact
        : Lower.Numerator == "0"
            ? SearchAvailabilityRowStatus.Uninformative
            : SearchAvailabilityRowStatus.Informative;
}

// Each certificate belongs only to this invocation's primary probabilities.
internal sealed record SearchAvailabilityCertificate(
    SearchAvailabilityStatus Status,
    string Detail,
    SearchAvailabilityInterval? Total = null,
    ImmutableArray<SearchAvailabilityInterval> Combos = default,
    ImmutableArray<SearchAvailabilityInterval> Groups = default
) {
    public string Reference => CardProbability.Internal.Certification.CalculationCapability.Reference;

    internal static SearchAvailabilityCertificate Unavailable(SearchAvailabilityStatus status, string detail) =>
        new(status, detail, null, [], []);
}
