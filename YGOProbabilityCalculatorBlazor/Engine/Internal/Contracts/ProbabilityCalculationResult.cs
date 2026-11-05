namespace CardProbability.Internal.Contracts;

/// <summary>
/// Contains the union probability and one standalone probability for every supplied combo,
/// in the same order as the input combo list.
/// </summary>
internal sealed record ProbabilityCalculationResult(
    double TotalProbability,
    IReadOnlyList<ComboProbabilityResult> ComboProbabilities,
    IReadOnlyList<GroupProbabilityResult>? GroupProbabilities = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    SearchAvailabilityCertificate? SearchAvailability = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    CalculationResultContract? Contract = null
);

/// <summary>The union probability of the active combos assigned to a named group.</summary>
internal sealed record GroupProbabilityResult(string GroupId, string GroupName, double Probability, int ActiveComboCount);

/// <summary>
/// The standalone probability for a combo at a specific position in the calculator input.
/// </summary>
/// <param name="ComboIndex">Zero-based position in the supplied combo list.</param>
/// <param name="ComboName">The combo's optional display name.</param>
/// <param name="Probability">Probability that this combo succeeds on its own.</param>
internal sealed record ComboProbabilityResult(int ComboIndex, string? ComboName, double Probability, string? GroupId = null);
