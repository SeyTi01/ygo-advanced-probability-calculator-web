using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.Interface;

public interface ILegacyCardMetadataEnricher {
    Task EnrichAsync(SessionState session);
}
