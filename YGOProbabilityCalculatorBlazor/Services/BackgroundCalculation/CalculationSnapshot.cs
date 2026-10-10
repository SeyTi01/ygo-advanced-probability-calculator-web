using System.Text.Json;
using System.Text.Json.Serialization;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;

// A separate, version-local wire contract. Never use session persistence for worker messages.
// The serialized value owns every nested input before the first asynchronous operation.
public sealed record CalculationSnapshot(string Json) {
    public static CalculationSnapshot Capture(
        IEnumerable<Card> cards,
        IEnumerable<Combo> combos,
        int handSize,
        IEnumerable<ComboGroup> groups,
        CalculationWorkPolicy? workPolicy = null
    ) {
        WorkerCard[] workerCards = cards
            .Where(card => card.Active)
            .Select(card => new WorkerCard(
                card.Id,
                card.Copies,
                card.Name,
                card.ExternalCardId,
                card.Categories.ToArray(),
                card.ManualMetadataCategoryKeys.ToArray(),
                card.DrawCount
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
        CalculationInput input = new(workerCards, workerCombos, handSize, workerGroups, workUnits);
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
    int? DrawCount = null
);

public sealed record WorkerCombo(
    string? Name,
    string? GroupId,
    ComboCategory[] Categories,
    ComboCard[] Cards,
    ComboAlternativeGroup[]? AlternativeGroups = null
);

public sealed record CalculationInput(
    WorkerCard[] Cards,
    WorkerCombo[] Combos,
    int HandSize,
    ComboGroup[] Groups,
    long? WorkUnits = null
);

public enum CalculationFailureKind { Input = 1, Error = 2 }

public sealed record CalculationResponse(
    ProbabilityCalculationResult? Result,
    string? Error,
    ProbabilityCalculationLimitReason? LimitReason = null,
    CalculationFailureKind? FailureKind = null
);

public static class CalculationWire {
    public static string Execute(string json) {
        CalculationResponse response;

        try {
            CalculationInput input = JsonSerializer.Deserialize(json, CalculationJsonContext.Default.CalculationInput)
                ?? throw new InvalidOperationException("Calculation input is missing.");

            List<Card> cards = input.Cards
                .Select(card => new Card(
                    card.Categories,
                    card.Copies,
                    card.Name,
                    true,
                    card.Id,
                    card.ExternalCardId,
                    card.ManualMetadataCategoryKeys,
                    card.DrawCount
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

            ProbabilityCalculationResult result = new ProbabilityCalculatorService().CalculateProbabilityResults(
                cards,
                combos,
                input.HandSize,
                input.Groups,
                workPolicy
            );

            response = new(result, null);
        }
        catch (ProbabilityCalculationLimitException ex) {
            response = new(null, ex.Message, ex.Reason);
        }
        catch (Exception ex) when (ex is ArgumentException or OverflowException) {
            response = new(null, ex.Message, FailureKind: CalculationFailureKind.Input);
        }
        catch (Exception ex) {
            response = new(null, ex.Message, FailureKind: CalculationFailureKind.Error);
        }

        return JsonSerializer.Serialize(response, CalculationJsonContext.Default.CalculationResponse);
    }

    public static ProbabilityCalculationResult ReadResult(string json) {
        CalculationResponse response = JsonSerializer.Deserialize(json, CalculationJsonContext.Default.CalculationResponse)
            ?? throw new InvalidOperationException("Background calculation returned no response.");

        bool hasInvalidResponse = response.Result is { ComboProbabilities: null } ||
            (response.LimitReason is { } reason && !Enum.IsDefined(reason)) ||
            (response.FailureKind is { } kind && !Enum.IsDefined(kind)) ||
            (response.LimitReason is not null && response.FailureKind is not null) ||
            (response.Result is not null && (response.Error is not null || response.LimitReason is not null || response.FailureKind is not null)) ||
            (response.Error is null && (response.LimitReason is not null || response.FailureKind is not null));

        if (hasInvalidResponse) {
            throw new InvalidOperationException("Background calculation returned an invalid response.");
        }

        if (response.LimitReason is { } limit) {
            throw new ProbabilityCalculationLimitException(limit);
        }

        if (response.FailureKind == CalculationFailureKind.Input) {
            throw new ArgumentException(response.Error);
        }

        if (response.Error is not null) {
            throw new InvalidOperationException(response.Error);
        }

        return response.Result ?? throw new InvalidOperationException("Background calculation returned no result.");
    }
}

[JsonSerializable(typeof(CalculationInput))]
[JsonSerializable(typeof(CalculationResponse))]
internal partial class CalculationJsonContext : JsonSerializerContext;
