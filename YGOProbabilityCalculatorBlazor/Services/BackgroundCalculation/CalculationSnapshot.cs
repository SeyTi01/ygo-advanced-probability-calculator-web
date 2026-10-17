using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;
using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;

// A separate, version-local wire contract. Never use session persistence for worker messages.
// The serialized value owns every nested input before the first asynchronous operation.
public sealed record CalculationSnapshot(string Json) {
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
                effects ? card.MinimumHandSize : 0
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
        CalculationInput input = new(workerCards, workerCombos, handSize, workerGroups, workUnits, storagePolicy?.Tier, options);
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
    int MinimumHandSize = 0
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
    CalculationOptions? Options = null
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
    public static string Execute(string json) => ExecuteCore(json, out _, false, null);

    // Version-local worker protocol, not a session option or a new public mode.
    internal static string ExecuteFastStage(string json, string? remainingWork = null) {
        string response = ExecuteCore(json, out long remaining, true, remainingWork);
        return JsonSerializer.Serialize(new AutomaticFastStage(response, remaining.ToString(CultureInfo.InvariantCulture)),
            CalculationJsonContext.Default.AutomaticFastStage);
    }

    private static string ExecuteCore(string json, out long remaining, bool fastStage, string? remainingWork) {
        CalculationResponse response;
        CalculationModel mode = CalculationModel.Fast;
        WorkBudget? budget = null;
        remaining = 0;

        try {
            ValidateOptions(json);
            CalculationInput input = JsonSerializer.Deserialize(json, CalculationJsonContext.Default.CalculationInput)
                ?? throw new InvalidOperationException("Calculation input is missing.");
            mode = input.Options?.Mode ?? CalculationModel.Fast;
            if (!Enum.IsDefined(mode)) {
                throw new ArgumentException("Unknown calculation mode.");
            }
            if (fastStage && mode != CalculationModel.Fast) {
                throw new ArgumentException("Automatic physical counting requires a Fast request.");
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
                    card.MinimumHandSize
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
            budget = new(workPolicy, storagePolicy);

            ProbabilityCalculationResult result = new ProbabilityCalculatorService().CalculateWithBudget(
                cards,
                combos,
                input.HandSize,
                input.Groups,
                budget,
                input.Options,
                physicalFast: remainingWork is not null
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
            remaining = budget is null ? 0 : budget.Allowance - budget.Spent;
        }

        return JsonSerializer.Serialize(response, CalculationJsonContext.Default.CalculationResponse);
    }

    public static ProbabilityCalculationResult ReadResult(string json, CalculationModel? requestedMode = null) {
        CalculationResponse response = JsonSerializer.Deserialize(json, CalculationJsonContext.Default.CalculationResponse)
            ?? throw new InvalidOperationException("Background calculation returned no response.");

        bool hasInvalidResponse = response.Result is { ComboProbabilities: null } ||
            (response.LimitReason is { } reason && !Enum.IsDefined(reason)) ||
            (response.FailureKind is { } kind && !Enum.IsDefined(kind)) ||
            (response.LimitReason is not null && response.FailureKind is not null) ||
            (response.Result is not null && response.Contract is not null) ||
            (response.Result is not null && (response.Error is not null || response.LimitReason is not null || response.FailureKind is not null)) ||
            (response.Error is null && (response.LimitReason is not null || response.FailureKind is not null));

        if (hasInvalidResponse) {
            throw new InvalidOperationException("Background calculation returned an invalid response.");
        }

        if (response.Contract is { } failure) {
            CalculationCompletion expected = response.LimitReason switch {
                ProbabilityCalculationLimitReason.Work => CalculationCompletion.WorkLimit,
                ProbabilityCalculationLimitReason.Storage => CalculationCompletion.StorageLimit,
                _ => response.FailureKind switch {
                    CalculationFailureKind.Unsupported => CalculationCompletion.Unsupported,
                    CalculationFailureKind.Cancelled => CalculationCompletion.Cancelled,
                    CalculationFailureKind.Input => CalculationCompletion.InvalidInput,
                    _ => CalculationCompletion.Error
                }
            };
            if (!ValidHeader(failure, requestedMode) || failure.Completion != expected
                || failure.Accuracy is not null || failure.Fractions is not null || failure.AutomaticExact is not null) {
                throw new InvalidOperationException("Invalid worker failure contract.");
            }
        }

        if (response.LimitReason is { } limit) {
            throw new ProbabilityCalculationLimitException(limit);
        }

        if (response.FailureKind == CalculationFailureKind.Input) {
            throw new ArgumentException(response.Error);
        }

        if (response.FailureKind == CalculationFailureKind.Unsupported) {
            if (response.Contract is not { Completion: CalculationCompletion.Unsupported, RequestedMode: CalculationModel.Exact,
                Accuracy: null, Fractions: null } contract) {
                throw new InvalidOperationException("Invalid unsupported calculation response.");
            }
            throw new UnsupportedExactCalculationException(contract);
        }
        if (response.FailureKind == CalculationFailureKind.Cancelled) {
            throw new OperationCanceledException(response.Error);
        }

        if (response.Error is not null) {
            throw new InvalidOperationException(response.Error);
        }

        ProbabilityCalculationResult result = response.Result ?? throw new InvalidOperationException("Background calculation returned no result.");
        ValidateResult(result, requestedMode);
        return result;
    }

    private static void ValidateResult(ProbabilityCalculationResult result, CalculationModel? requestedMode) {
        // Legacy native result constructors remain available; version-local worker
        // success always carries the complete immutable contract.
        CalculationResultContract contract = result.Contract ?? throw new InvalidOperationException("Worker result has no accuracy contract.");
        bool valid = ValidHeader(contract, requestedMode)
            && contract.Completion == CalculationCompletion.Success && contract.Accuracy is { } accuracy && Enum.IsDefined(accuracy)
            && Enum.IsDefined(contract.Scope) && !string.IsNullOrWhiteSpace(contract.Reason)
            && contract.Fractions is { Combos.IsDefault: false, Groups.IsDefault: false } fractions
            && fractions.Combos.Length == result.ComboProbabilities.Count
            && fractions.Groups.Length == (result.GroupProbabilities?.Count ?? 0);
        if (!valid || !ValidProbability(result.TotalProbability)
            || result.ComboProbabilities.Any(row => row is null || !ValidProbability(row.Probability))
            || result.GroupProbabilities?.Any(row => row is null || !ValidProbability(row.Probability)) == true) {
            throw new InvalidOperationException("Invalid worker accuracy contract or rows.");
        }
        foreach (ProbabilityFraction fraction in new[] { contract.Fractions!.Total }.Concat(contract.Fractions.Combos).Concat(contract.Fractions.Groups)) {
            if (!ValidFraction(fraction)) {
                throw new InvalidOperationException("Invalid worker rational value.");
            }
        }
        if (contract.AutomaticExact is { } automatic && (!Enum.IsDefined(automatic)
            || contract.ExecutedMode != CalculationModel.Fast
            || automatic == AutomaticExactOutcome.Completed && contract.Accuracy != CalculationAccuracy.Exact)) {
            throw new InvalidOperationException("Invalid automatic Exact outcome.");
        }
        SearchAvailabilityCertificate? certificate = result.SearchAvailability;
        if (contract.ExecutedMode != CalculationModel.Fast && (contract.Accuracy != CalculationAccuracy.Exact || certificate is not null)
            || contract.Accuracy == CalculationAccuracy.Exact && contract.Proof is null && certificate?.Status != SearchAvailabilityStatus.Exact
            || contract.Accuracy == CalculationAccuracy.Uncertified && certificate?.Status is not
                (SearchAvailabilityStatus.NotCertified or SearchAvailabilityStatus.Limited or SearchAvailabilityStatus.Error)
            || contract.Accuracy == CalculationAccuracy.Bounded && certificate?.Status != SearchAvailabilityStatus.Bounded) {
            throw new InvalidOperationException("Worker accuracy contradicts its proof or certificate.");
        }
        if (certificate is not null) {
            if (!Enum.IsDefined(certificate.Status) || certificate.Combos.IsDefault || certificate.Groups.IsDefault) {
                throw new InvalidOperationException("Invalid worker certificate.");
            }
            bool hasIntervals = certificate.Status is SearchAvailabilityStatus.Bounded or SearchAvailabilityStatus.Exact or SearchAvailabilityStatus.NotApplicable;
            if (hasIntervals) {
                if (certificate.Total is null || certificate.Combos.Length != result.ComboProbabilities.Count
                    || certificate.Groups.Length != (result.GroupProbabilities?.Count ?? 0)) {
                    throw new InvalidOperationException("Incomplete worker intervals.");
                }
                SearchAvailabilityInterval[] rows = [certificate.Total, .. certificate.Combos, .. certificate.Groups];
                ProbabilityFraction[] primary = [contract.Fractions.Total, .. contract.Fractions.Combos, .. contract.Fractions.Groups];
                for (int index = 0; index < rows.Length; index++) {
                    SearchAvailabilityInterval row = rows[index];
                    // A completed necessary-event proof can reduce U below the
                    // unchanged optimistic primary. Still require L <= U <= primary.
                    if (row is null || !ValidFraction(row.Lower) || !ValidFraction(row.Upper)
                        || BigInteger.Parse(row.Upper.Numerator, CultureInfo.InvariantCulture) * BigInteger.Parse(primary[index].Denominator, CultureInfo.InvariantCulture)
                            > BigInteger.Parse(primary[index].Numerator, CultureInfo.InvariantCulture) * BigInteger.Parse(row.Upper.Denominator, CultureInfo.InvariantCulture)
                        || BigInteger.Parse(row.Lower.Numerator, CultureInfo.InvariantCulture) * BigInteger.Parse(row.Upper.Denominator, CultureInfo.InvariantCulture)
                            > BigInteger.Parse(row.Upper.Numerator, CultureInfo.InvariantCulture) * BigInteger.Parse(row.Lower.Denominator, CultureInfo.InvariantCulture)
                        || certificate.Status == SearchAvailabilityStatus.Exact && !row.IsExact) {
                        throw new InvalidOperationException("Invalid worker interval.");
                    }
                }
            }
            else if (certificate.Total is not null || !certificate.Combos.IsEmpty || !certificate.Groups.IsEmpty) {
                throw new InvalidOperationException("Unavailable certificate contains partial intervals.");
            }
        }
    }

    private static bool ValidProbability(double value) => double.IsFinite(value) && value is >= 0 and <= 1;

    private static bool ValidHeader(CalculationResultContract contract, CalculationModel? requestedMode) =>
        contract.Version == CalculationResultContract.CurrentVersion
        && Enum.IsDefined(contract.RequestedMode) && contract.RequestedMode == contract.ExecutedMode
        && (requestedMode is null || contract.RequestedMode == requestedMode)
        && Enum.IsDefined(contract.Completion)
        && contract.Reference == (contract.ExecutedMode == CalculationModel.OpeningHand
            ? CalculationResultContract.OpeningReference : CalculationResultContract.EffectsReference)
        && contract.Scope == (contract.ExecutedMode == CalculationModel.OpeningHand
            ? CalculationRuleScope.OpeningHand : CalculationRuleScope.SavedEffectsFixedPolicy);

    private static bool ValidFraction(ProbabilityFraction? value) => value is not null
        && BigInteger.TryParse(value.Numerator, NumberStyles.None, CultureInfo.InvariantCulture, out BigInteger numerator)
        && BigInteger.TryParse(value.Denominator, NumberStyles.None, CultureInfo.InvariantCulture, out BigInteger denominator)
        && denominator > 0 && numerator >= 0 && numerator <= denominator;

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
