using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;

public interface IBackgroundCalculator : IAsyncDisposable {
    Task<ProbabilityCalculationResult> CalculateAsync(CalculationSnapshot snapshot, CancellationToken cancellationToken);
}
