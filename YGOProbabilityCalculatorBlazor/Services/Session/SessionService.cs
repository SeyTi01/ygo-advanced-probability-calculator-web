using System.Text.Json;
using Microsoft.JSInterop;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Services.Session;

public class SessionService(IJSRuntime jsRuntime) : ISessionService {
    public async Task SaveSessionAsync(CalculatorSession session, string fileName) {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("File name cannot be empty", nameof(fileName));

        if (!fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            fileName += ".json";

        var options = new JsonSerializerOptions {
            WriteIndented = true
        };

        var json = JsonSerializer.Serialize(session, options);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);

        var base64 = Convert.ToBase64String(bytes);

        await jsRuntime.InvokeVoidAsync("downloadFileFromStream", fileName, base64);
    }

    public Task<CalculatorSession> LoadSessionAsync(string fileContent) {
        try {
            var options = new JsonSerializerOptions {
                PropertyNameCaseInsensitive = true
            };

            var session = JsonSerializer.Deserialize<CalculatorSession>(fileContent, options);
            if (session == null)
                throw new InvalidOperationException("Failed to deserialize session data");

            return Task.FromResult(session);
        }
        catch (JsonException ex) {
            throw new InvalidOperationException("Invalid session file format", ex);
        }
    }
}
