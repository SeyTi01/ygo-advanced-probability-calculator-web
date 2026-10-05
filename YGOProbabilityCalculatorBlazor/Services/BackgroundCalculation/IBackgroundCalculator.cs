using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;

public interface IBackgroundCalculator
{
    Task<ProbabilityCalculationResult> CalculateAsync(
        CalculationSnapshot snapshot,
        CancellationToken cancellationToken);
}
