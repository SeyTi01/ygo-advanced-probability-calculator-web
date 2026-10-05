namespace YGOProbabilityCalculatorBlazor.Services.Interface;

public interface ICardArtworkService
{
    Task<string?> GetArtworkUrlAsync(int externalCardId);
}
