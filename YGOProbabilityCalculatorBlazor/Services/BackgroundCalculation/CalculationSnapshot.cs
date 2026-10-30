using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;

namespace YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;

// A separate, version-local wire contract. Never use session persistence for worker messages.
// The serialized value owns every nested input before the first asynchronous operation.
public sealed record CalculationSnapshot(string Json) {
    // Presentation policy only; never serialized into the mathematical worker input.
    public bool AllowAutomaticExact { get; init; } = true;
    internal CalculationModel RequestedMode {
        get {
            using JsonDocument document = JsonDocument.Parse(Json);
            if (!document.RootElement.TryGetProperty("Options", out JsonElement options) || options.ValueKind == JsonValueKind.Null
                || !options.TryGetProperty("Mode", out JsonElement mode)) {
                return CalculationModel.Fast;
            }
            if (!mode.TryGetInt32(out int value) || !Enum.IsDefined((CalculationModel)value)) {
                throw new ArgumentException("Unknown calculation mode in snapshot.");
            }
            return (CalculationModel)value;
        }
    }
    public static CalculationSnapshot Capture(
        IEnumerable<Card> cards,
        IEnumerable<Combo> combos,
        int handSize,
        IEnumerable<ComboGroup> groups,
        CalculationWorkPolicy? workPolicy = null,
        bool? drawEffectsEnabled = null,
        CalculationStoragePolicy? storagePolicy = null,
        CalculationOptions? options = null,
        IEnumerable<CategoryBase>? categoryDefinitions = null
    ) {
        CalculationModel mode = options?.Mode ?? CalculationModel.Fast;
        if (!Enum.IsDefined(mode)) {
            throw new ArgumentOutOfRangeException(nameof(options), "Unknown calculation mode.");
        }
        if (options?.CertifySearchAvailability == true && mode != CalculationModel.Fast) {
            throw new ArgumentException("The legacy certification flag is only compatible with Fast mode.", nameof(options));
        }
        // Temporary native Capture compatibility adapter; the wire owns only Mode.
        if (drawEffectsEnabled is { } legacy) {
            if (mode != CalculationModel.Fast) {
                throw new ArgumentException("Do not combine legacy draw enablement with an explicit mode.");
            }
            mode = legacy ? CalculationModel.Fast : CalculationModel.OpeningHand;
        }
        bool effects = mode != CalculationModel.OpeningHand;
        options = (options ?? new()) with { Mode = mode, CertifySearchAvailability = false };
        WorkerCard[] workerCards = CategoryMembership.Materialize(cards, categoryDefinitions ?? [])
            .Where(card => card.Active)
            .Select(card => card.ForCalculation(effects))
            .Select(card => new WorkerCard(
                card.Id,
                card.Copies,
                card.Name,
                card.ExternalCardId,
                card.Categories.Select(category => new CategoryBase(category.Name, category.Source, category.MetadataKey)).ToArray(),
                card.ManualMetadataCategoryKeys.ToArray(),
                effects ? card.DrawCount : null,
                effects ? card.DrawOncePerTurn : true,
                effects ? card.PostDrawDiscardCount : 0,
                effects ? card.PostDrawSelector : CardSelector.Any,
                card.PostDrawFailureBehavior,
                effects ? card.PreDrawRemovalCount : 0,
                effects ? card.PreDrawSelector : CardSelector.Any,
                effects ? card.PreDrawDeckRemovalCount : 0,
                effects && card.EffectProfile != EffectProfile.Legacy ? card.EffectProfile : null,
                effects ? card.PostDrawDeckMillCount : 0,
                effects && card.FirstActivationOnly,
                effects && card.BlocksLaterDraws,
                effects ? card.Search : null,
                effects && card.Search is not null ? card.OncePerTurn : null,
                effects ? card.EffectPlan.ActivationLimit : null,
                effects ? card.MinimumHandSize : 0,
                effects ? card.EffectPlan.Operation : DrawOperation.Ordinary
            ))
            .ToArray();

        WorkerCombo[] workerCombos = combos
            .Where(combo => combo.Active)
            .Select(combo => new WorkerCombo(
                combo.Name,
                combo.GroupId,
                combo.Categories.ToArray(),
                combo.Cards.ToArray(),
                combo.AlternativeGroups.ToArray()
            ))
            .ToArray();

        ComboGroup[] workerGroups = groups.ToArray();
        long workUnits = (workPolicy ?? CalculationWorkPolicy.Default).WorkUnits;
        CalculationInput input = new(workerCards, workerCombos, handSize, workerGroups, workUnits, storagePolicy?.Tier, options,
            workerCards.Any(card => card.DrawOperation == DrawOperation.DangerReveal) ? 1 : null);
        string json = JsonSerializer.Serialize(input, CalculationJsonContext.Default.CalculationInput);

        return new(json);
    }
}

public sealed record WorkerCard(
    string Id,
    int Copies,
    string? Name,
    int? ExternalCardId,
    CategoryBase[] Categories,
    string[] ManualMetadataCategoryKeys,
    int? DrawCount = null,
    bool DrawOncePerTurn = true,
    int PostDrawDiscardCount = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CardSelector? PostDrawSelector = null,
    PostDrawFailureBehavior PostDrawFailureBehavior = PostDrawFailureBehavior.FailBranch,
    int PreDrawRemovalCount = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CardSelector? PreDrawSelector = null,
    int PreDrawDeckRemovalCount = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] EffectProfile? EffectProfile = null,
    int PostDrawDeckMillCount = 0,
    bool FirstActivationOnly = false,
    bool BlocksLaterDraws = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SearchEffectPlan? Search = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? OncePerTurn = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? ActivationLimit = null,
    int MinimumHandSize = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] DrawOperation DrawOperation = DrawOperation.Ordinary
);

public sealed record WorkerCombo(
    string? Name,
    string? GroupId,
    ComboCategory[] Categories,
    ComboCard[] Cards,
    ComboAlternativeGroup[]? AlternativeGroups = null
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CalculationInput(
    WorkerCard[] Cards,
    WorkerCombo[] Combos,
    int HandSize,
    ComboGroup[] Groups,
    long? WorkUnits = null,
    CalculationStorageTier? StorageTier = null,
    CalculationOptions? Options = null,
    // Older workers reject unknown top-level fields. Only the new operation
    // emits this marker, preserving ordinary wire fixtures byte-for-byte.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? DrawOperationVersion = null
);

public enum CalculationFailureKind { Input = 1, Error = 2, Unsupported = 3, Cancelled = 4 }

public sealed record CalculationResponse(
    ProbabilityCalculationResult? Result,
    string? Error,
    ProbabilityCalculationLimitReason? LimitReason = null,
    CalculationFailureKind? FailureKind = null,
    CalculationResultContract? Contract = null
);

public static class CalculationWire {
    private enum WorkExecution { Bounded, InteractiveExact, ProgressiveExact }

    // Trusted worker entry points select unlimited execution. The snapshot stays
    // finite and strictly validated, including on these entry points.
    internal static string ExecuteInteractiveExact(string json) =>
        ExecuteCore(json, out _, false, null, WorkExecution.InteractiveExact);

    internal static string ExecuteProgressiveUpgrade(string json) {
        string response = ExecuteCore(json, out _, true, null, WorkExecution.ProgressiveExact);
        return JsonSerializer.Serialize(new AutomaticFastStage(response, "0"),
            CalculationJsonContext.Default.AutomaticFastStage);
    }

    public static string Execute(string json) => ExecuteCore(json, out _, false, null);

    // Version-local worker protocol, not a session option or a new public mode.
    internal static string ExecuteFastStage(string json, string? remainingWork = null) {
        string response = ExecuteCore(json, out long remaining, true, remainingWork);
        return JsonSerializer.Serialize(new AutomaticFastStage(response, remaining.ToString(CultureInfo.InvariantCulture)),
            CalculationJsonContext.Default.AutomaticFastStage);
    }

    private static string ExecuteCore(
        string json,
        out long remaining,
        bool fastStage,
        string? remainingWork,
        WorkExecution execution = WorkExecution.Bounded
    ) {
        CalculationResponse response;
        CalculationModel mode = CalculationModel.Fast;
        WorkBudget? budget = null;
        remaining = 0;

        try {
            ValidateOptions(json);
            CalculationInput input = JsonSerializer.Deserialize(json, CalculationJsonContext.Default.CalculationInput)
                ?? throw new InvalidOperationException("Calculation input is missing.");
            if (input.DrawOperationVersion is not null and not 1
                || (input.Cards.Any(card => card.DrawOperation == DrawOperation.DangerReveal) && input.DrawOperationVersion != 1)) {
                throw new ArgumentException("Unsupported or missing worker draw operation version.");
            }
            mode = input.Options?.Mode ?? CalculationModel.Fast;
            if (!Enum.IsDefined(mode)) {
                throw new ArgumentException("Unknown calculation mode.");
            }
            if (fastStage && mode != CalculationModel.Fast) {
                throw new ArgumentException("Automatic physical counting requires a Fast request.");
            }

            if (execution == WorkExecution.InteractiveExact && mode != CalculationModel.Exact) {
                throw new ArgumentException("Interactive Exact requires an Exact request.");
            }

            List<Card> cards = input.Cards
                .Select(card => new Card(
                    card.Categories,
                    card.Copies,
                    card.Name,
                    true,
                    card.Id,
                    card.ExternalCardId,
                    card.ManualMetadataCategoryKeys,
                    card.DrawCount,
                    card.DrawOncePerTurn,
                    card.PostDrawDiscardCount,
                    card.PostDrawSelector,
                    card.PostDrawFailureBehavior,
                    card.PreDrawRemovalCount,
                    card.PreDrawSelector,
                    card.PreDrawDeckRemovalCount,
                    card.EffectProfile,
                    card.PostDrawDeckMillCount,
                    card.FirstActivationOnly,
                    card.BlocksLaterDraws,
                    card.Search,
                    card.OncePerTurn,
                    card.ActivationLimit,
                    card.MinimumHandSize,
                    drawOperation: card.DrawOperation
                ))
                .ToList();

            List<Combo> combos = input.Combos
                .Select(combo => new Combo(
                    combo.Categories,
                    combo.Name,
                    true,
                    combo.GroupId,
                    combo.Cards,
                    combo.AlternativeGroups
                ))
                .ToList();

            CalculationWorkPolicy workPolicy = input.WorkUnits is long units
                ? new(units)
                : CalculationWorkPolicy.Default;
            CalculationStoragePolicy storagePolicy = CalculationStoragePolicy.ForTier(
                input.StorageTier ?? CalculationStorageTier.Standard);
            if (remainingWork is not null) {
                if (!long.TryParse(remainingWork, NumberStyles.None, CultureInfo.InvariantCulture, out long allowance)
                    || allowance <= 0 || allowance > workPolicy.WorkUnits) {
                    throw new ArgumentException("Invalid remaining automatic calculation Work.");
                }
                workPolicy = new(allowance);
            }
            budget = new(execution == WorkExecution.Bounded ? workPolicy : CalculationWorkPolicy.Unlimited, storagePolicy);

            ProbabilityCalculationResult result = new ProbabilityCalculatorService().CalculateWithBudget(
                cards,
                combos,
                input.HandSize,
                input.Groups,
                budget,
                input.Options,
                physicalFast: remainingWork is not null || execution == WorkExecution.ProgressiveExact
            );

            response = new(result, null);
        }
        catch (UnsupportedExactCalculationException ex) {
            response = new(null, ex.Message, FailureKind: CalculationFailureKind.Unsupported, Contract: ex.Contract);
        }
        catch (ProbabilityCalculationLimitException ex) {
            response = new(null, ex.Message, ex.Reason, Contract: CalculationResultContract.Failure(mode,
                ex.Reason == ProbabilityCalculationLimitReason.Work ? CalculationCompletion.WorkLimit : CalculationCompletion.StorageLimit, ex.Message));
        }
        catch (OperationCanceledException ex) {
            response = new(null, "Calculation cancelled.", FailureKind: CalculationFailureKind.Cancelled,
                Contract: CalculationResultContract.Failure(mode, CalculationCompletion.Cancelled, ex.Message));
        }
        catch (Exception ex) when (ex is ArgumentException or OverflowException or JsonException) {
            response = new(null, ex.Message, FailureKind: CalculationFailureKind.Input);
        }
        catch (Exception ex) {
            response = new(null, ex.Message, FailureKind: CalculationFailureKind.Error);
        }
        finally {
            remaining = budget?.Remaining ?? 0;
        }

        return JsonSerializer.Serialize(response, CalculationJsonContext.Default.CalculationResponse);
    }

    public static ProbabilityCalculationResult ReadResult(string json, CalculationModel? requestedMode = null) =>
        CalculationResponseReader.Read(json, requestedMode);

    private static void ValidateOptions(string json) {
        using JsonDocument document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object) {
            throw new JsonException("Calculation input must be an object.");
        }
        HashSet<string> inputNames = new(StringComparer.OrdinalIgnoreCase);
        foreach (JsonProperty property in document.RootElement.EnumerateObject()) {
            if (!inputNames.Add(property.Name)) {
                throw new JsonException("Duplicated worker calculation field: " + property.Name);
            }
        }
        if (document.RootElement.TryGetProperty("Cards", out JsonElement cards) && cards.ValueKind == JsonValueKind.Array) {
            foreach (JsonElement card in cards.EnumerateArray()) {
                if (card.ValueKind != JsonValueKind.Object) {
                    continue;
                }
                JsonProperty[] operations = card.EnumerateObject()
                    .Where(property => property.Name.Equals("DrawOperation", StringComparison.OrdinalIgnoreCase)).ToArray();
                if (operations.Length > 1 || (operations.Length == 1 && operations[0].Name != "DrawOperation")) {
                    throw new JsonException("Unknown or duplicated worker draw operation field.");
                }
            }
        }
        if (document.RootElement.TryGetProperty("Options", out JsonElement options) && options.ValueKind == JsonValueKind.Object) {
            HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
            foreach (JsonProperty property in options.EnumerateObject()) {
                if (!names.Add(property.Name) || property.Name is not ("Mode" or "DrawActivationPriority")) {
                    throw new JsonException("Unknown, obsolete or duplicated worker calculation option: " + property.Name);
                }
            }
        }
    }
}

[JsonSerializable(typeof(CategoryBase))]
[JsonSerializable(typeof(CategoryBase[]))]
[JsonSerializable(typeof(IReadOnlyList<CategoryBase>))]
[JsonSerializable(typeof(CalculationInput))]
[JsonSerializable(typeof(CalculationResponse))]
[JsonSerializable(typeof(AutomaticFastStage))]
internal partial class CalculationJsonContext : JsonSerializerContext;

internal sealed record AutomaticFastStage(string Response, string RemainingWork);
