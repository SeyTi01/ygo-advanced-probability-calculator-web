using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.Interface;

public interface IPendingSessionService {
    SessionState? PendingSession { get; set; }
}
