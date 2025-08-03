using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Services.Session;

public class PendingSessionService : IPendingSessionService {
    public SessionState? PendingSession { get; set; }
}
