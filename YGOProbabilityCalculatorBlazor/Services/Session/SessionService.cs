using YGOProbabilityCalculatorBlazor.Localization;
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
        if (string.IsNullOrWhiteSpace(fileName)) {
            throw new ArgumentException(UiText.Get("Ui.FileNameCannotBeEmpty"), nameof(fileName));
        }

        if (!fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) {
            fileName += ".json";
        }

        string json = SerializeSession(session);
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);
        string base64 = Convert.ToBase64String(bytes);

        await jsRuntime.InvokeVoidAsync("saveSessionFile", fileName, base64);
    }

    // Offline codec shared by file saving and recovery; never opens a picker.
    public string SerializeSession(SessionState session) {
        if (!Enum.IsDefined(session.CalculationModel) || !Enum.IsDefined(session.FastExecutionStrategy)
            || (session.CalculationModel != CalculationModel.Fast && session.FastExecutionStrategy != FastExecutionStrategy.Progressive)) {
            throw new ArgumentOutOfRangeException(nameof(session), UiText.Get("Ui.UnknownCalculationMode"));
        }
        SessionState sessionToSave = new() {
            SchemaVersion = SessionState.CurrentSchemaVersion,
            Categories = session.Categories,
            Cards = session.Cards,
            Combos = session.Combos,
            ComboGroups = session.ComboGroups,
            HandSize = session.HandSize,
            CalculationModel = session.CalculationModel,
            FastExecutionStrategy = session.FastExecutionStrategy,
            DrawActivationPriority = (session.DrawActivationPriority ?? DrawActivationPriority.Default).ForWorkspace(session.Cards),
            CategoryColorIndices = session.CategoryColorIndices
        };

        return serializer.Serialize(sessionToSave, _serializerOptions);
    }

    public Task<SessionState> LoadSessionAsync(string fileContent) {
        try {
            string migratedJson = _schemaMigrator.MigrateToCurrent(fileContent);
            SessionState session = serializer.Deserialize<SessionState>(migratedJson, _serializerOptions) ?? throw new InvalidOperationException(UiText.Get("Ui.FailedToDeserializeSessionData"));

            // Reject incomplete model graphs before any caller begins replacing its workspace.
            // Omitted collections retain defaults; optional legacy groups/colors may still be null.
            bool hasMissingCollectionsOrEntries = session.Categories is null
                || session.Cards is null
                || session.Combos is null
                || session.Categories.Any(category => category is null)
                || session.Cards.Any(card => card is null)
                || session.Combos.Any(combo => combo is null)
                || session.ComboGroups?.Any(group => group is null) == true;

            if (hasMissingCollectionsOrEntries) {
                throw new JsonException(UiText.Get("Ui.SessionContainsMissingModelCollectionsOrEntries"));
            }

            bool hasMissingComboRequirements = session.Combos.Any(combo =>
                combo.Categories.Any(category => category is null) || combo.Cards.Any(card => card is null)
            );

            if (hasMissingComboRequirements) {
                throw new JsonException(UiText.Get("Ui.SessionContainsMissingComboRequirements"));
            }

            // These identities are rendered as sibling keys and used for editor references.
            // Reject ambiguity before the caller clears its accepted workspace.
            bool hasDuplicateCategoryIdentities = session.Categories
                .Select(static category => category.Identity)
                .Distinct(StringComparer.Ordinal)
                .Count() != session.Categories.Count;

            if (hasDuplicateCategoryIdentities) {
                throw new JsonException(UiText.Get("Ui.SessionContainsDuplicateCategoryIdentities"));
            }

            if (session.ComboGroups is { } groups) {
                bool hasInvalidComboGroup = groups.Any(group => string.IsNullOrWhiteSpace(group.Id)
                    || string.IsNullOrWhiteSpace(group.Name))
                    || groups.Select(static group => group.Id)
                        .Distinct(StringComparer.Ordinal)
                        .Count() != groups.Count;

                if (hasInvalidComboGroup) {
                    throw new JsonException(UiText.Get("Ui.SessionContainsInvalidOrDuplicateComboGroupIdentities"));
                }
            }

            bool hasDuplicateCardIds = session.Cards
                .Select(static card => card.Id)
                .Distinct(StringComparer.Ordinal)
                .Count() != session.Cards.Count;

            if (hasDuplicateCardIds) {
                throw new InvalidOperationException(UiText.Get("Ui.SessionContainsDuplicateCardIDs"));
            }

            return Task.FromResult(session);
        }
        catch (JsonException ex) {
            throw new InvalidOperationException(UiText.Get("Ui.InvalidSessionFileFormat"), ex);
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
                new ComboCategoryConverter(),
                new ComboCardConverter()
            }
        };
    }
}
