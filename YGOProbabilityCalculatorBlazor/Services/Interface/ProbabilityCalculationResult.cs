namespace YGOProbabilityCalculatorBlazor.Services.Interface;

/// <summary>
/// Contains the union probability and one standalone probability for every supplied combo,
/// in the same order as the input combo list.
/// </summary>
public sealed record ProbabilityCalculationResult(
    double TotalProbability,
    IReadOnlyList<ComboProbabilityResult> ComboProbabilities);

/// <summary>
/// The standalone probability for a combo at a specific position in the calculator input.
/// </summary>
/// <param name="ComboIndex">Zero-based position in the supplied combo list.</param>
/// <param name="ComboName">The combo's optional display name.</param>
/// <param name="Probability">Probability that this combo succeeds on its own.</param>
public sealed record ComboProbabilityResult(int ComboIndex, string? ComboName, double Probability);
