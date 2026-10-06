using System.Numerics;

namespace YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

internal readonly record struct CountBound(BigInteger EligibleRows, int MinCount, int MaxCount);

// Roles are optional compilation metadata for safe OR factoring; event
// equality and cached counts depend only on the compiled hand-wide bounds.
internal sealed record CompiledEvent(CountBound[] Constraints, CountBound[]? Roles = null)
{
    public bool Equals(CompiledEvent? other) =>
        other is not null && Constraints.AsSpan().SequenceEqual(other.Constraints);

    public override int GetHashCode()
    {
        HashCode hash = new();

        foreach (CountBound constraint in Constraints)
        {
            hash.Add(constraint);
        }

        return hash.ToHashCode();
    }
}
