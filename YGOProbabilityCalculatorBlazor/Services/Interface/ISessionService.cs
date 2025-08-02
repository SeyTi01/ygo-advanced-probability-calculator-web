using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.Interface;

public interface ISessionService {
    Task SaveSessionAsync(CalculatorSession session, string fileName);
    Task<CalculatorSession> LoadSessionAsync(string fileName);
}
