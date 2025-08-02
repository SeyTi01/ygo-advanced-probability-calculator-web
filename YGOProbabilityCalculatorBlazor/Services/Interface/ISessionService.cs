using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.Interface;

public interface ISessionService {
    Task SaveSessionAsync(SessionState sessionState, string fileName);
    Task<SessionState> LoadSessionAsync(string fileName);
}
