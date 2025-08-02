using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Services.Session;

public class SessionService(IFileService fileService) : ISessionService {
    public async Task SaveSessionAsync(CalculatorSession session, string fileName) {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("File name cannot be empty", nameof(fileName));

        if (!fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            fileName += ".json";

        var options = new JsonSerializerOptions {
            WriteIndented = true
        };

        var json = JsonSerializer.Serialize(session, options);
        await fileService.WriteAllTextAsync(fileName, json);
    }

    public async Task<CalculatorSession> LoadSessionAsync(string fileName) {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("File name cannot be empty", nameof(fileName));

        if (!fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            fileName += ".json";

        var json = await fileService.ReadAllTextAsync(fileName);

        var options = new JsonSerializerOptions {
            PropertyNameCaseInsensitive = true
        };

        var session = JsonSerializer.Deserialize<CalculatorSession>(json, options);
        if (session == null)
            throw new InvalidOperationException("Failed to deserialize session data");

        return session;
    }
}
