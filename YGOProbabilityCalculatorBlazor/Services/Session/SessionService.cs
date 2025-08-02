using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Converter;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Services.Session;

public class SessionService(IJSRuntime jsRuntime) : ISessionService {
    private static JsonSerializerOptions CreateSerializerOptions() {
        return new JsonSerializerOptions {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Converters = {
                new JsonStringEnumConverter(),
                new CategoryBaseConverter(),
                new CardConverter(),
                new ComboConverter(),
                new ComboCategoryConverter()
            }
        };
    }

    public async Task SaveSessionAsync(SessionState session, string fileName) {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("File name cannot be empty", nameof(fileName));

        if (!fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            fileName += ".json";

        var options = CreateSerializerOptions();
        var json = JsonSerializer.Serialize(session, options);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        var base64 = Convert.ToBase64String(bytes);

        await jsRuntime.InvokeVoidAsync("downloadFileFromStream", fileName, base64);
    }

    public Task<SessionState> LoadSessionAsync(string fileContent) {
        try {
            var options = CreateSerializerOptions();
            var session = JsonSerializer.Deserialize<SessionState>(fileContent, options);

            if (session == null)
                throw new InvalidOperationException("Failed to deserialize session data");

            return Task.FromResult(session);
        }
        catch (JsonException ex) {
            throw new InvalidOperationException("Invalid session file format", ex);
        }
    }
}
