using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Converter;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Services.Session;

public class SessionService(IJSRuntime jsRuntime, ISerializer serializer) : ISessionService {
    private readonly JsonSerializerOptions _serializerOptions = CreateSerializerOptions();

    public async Task SaveSessionAsync(SessionState session, string fileName) {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("File name cannot be empty", nameof(fileName));

        if (!fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            fileName += ".json";

        var json = serializer.Serialize(session, _serializerOptions);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        var base64 = Convert.ToBase64String(bytes);

        await jsRuntime.InvokeVoidAsync("downloadFileFromStream", fileName, base64);
    }

    public Task<SessionState> LoadSessionAsync(string fileContent) {
        try {
            var session = serializer.Deserialize<SessionState>(fileContent, _serializerOptions);

            if (session == null)
                throw new InvalidOperationException("Failed to deserialize session data");

            return Task.FromResult(session);
        }
        catch (JsonException ex) {
            throw new InvalidOperationException("Invalid session file format", ex);
        }
    }

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
}
