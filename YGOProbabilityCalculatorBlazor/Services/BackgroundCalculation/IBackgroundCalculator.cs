using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;

public interface IBackgroundCalculator : IAsyncDisposable {
    Task<ProbabilityCalculationResult> CalculateAsync(CalculationSnapshot snapshot, CancellationToken cancellationToken);
}

// Opt-in keeps existing calculators, mocks and one-shot probes unchanged.
public interface IProgressiveBackgroundCalculator : IBackgroundCalculator {
    Task<ProbabilityCalculationResult> CalculateProgressivelyAsync(
        CalculationSnapshot snapshot,
        Func<ProbabilityCalculationResult, Task> onPreliminary,
        CancellationToken cancellationToken
    );
}
