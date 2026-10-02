using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Converter;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazor.Services.Session;

public class SessionService(IJSRuntime jsRuntime, ISerializer serializer) : ISessionService {
    private readonly JsonSerializerOptions _serializerOptions = CreateSerializerOptions();
    private readonly SessionSchemaMigrator _schemaMigrator = new();

    public async Task SaveSessionAsync(SessionState session, string fileName) {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("File name cannot be empty", nameof(fileName));

        if (!fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            fileName += ".json";

        var sessionToSave = new SessionState {
            SchemaVersion = SessionState.CurrentSchemaVersion,
            Categories = session.Categories,
            Cards = session.Cards,
            Combos = session.Combos,
            ComboGroups = session.ComboGroups,
            HandSize = session.HandSize,
            CategoryColorIndices = session.CategoryColorIndices
        };
        var json = serializer.Serialize(sessionToSave, _serializerOptions);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        var base64 = Convert.ToBase64String(bytes);

        await jsRuntime.InvokeVoidAsync("saveSessionFile", fileName, base64);
    }

    public Task<SessionState> LoadSessionAsync(string fileContent) {
        try {
            var migratedJson = _schemaMigrator.MigrateToCurrent(fileContent);
            var session = serializer.Deserialize<SessionState>(migratedJson, _serializerOptions);

            if (session == null)
                throw new InvalidOperationException("Failed to deserialize session data");

            if (session.Cards.Select(card => card.Id).Distinct(StringComparer.Ordinal).Count() != session.Cards.Count)
                throw new InvalidOperationException("Session contains duplicate card IDs.");

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
