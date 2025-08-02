namespace YGOProbabilityCalculatorBlazor.Services.Interface;

public interface ICardInfoService {
    Task<string> GetCardNameAsync(int id);
}
