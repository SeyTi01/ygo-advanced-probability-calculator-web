using CardProbability.Internal.Model;

namespace CardProbability.Internal.Contracts;

// Execution choices owned by this invocation of the mathematical runtime.
internal sealed record CalculationOptions(
    DrawActivationPriority? DrawActivationPriority = null,
    bool CertifySearchAvailability = false,
    CalculationModel Mode = CalculationModel.Fast
);
