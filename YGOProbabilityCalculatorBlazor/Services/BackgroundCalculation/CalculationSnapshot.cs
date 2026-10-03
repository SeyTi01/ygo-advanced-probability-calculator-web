using System.Text.Json;
using System.Text.Json.Serialization;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;

// A separate, version-local wire contract. Never use session persistence for worker messages.
// The serialized value owns every nested input before the first asynchronous operation.
public sealed record CalculationSnapshot(string Json) {
    public static CalculationSnapshot Capture(IEnumerable<Card> cards, IEnumerable<Combo> combos,
        int handSize, IEnumerable<ComboGroup> groups) => new(JsonSerializer.Serialize(new CalculationInput(
            cards.Where(c => c.Active).Select(c => new WorkerCard(c.Id, c.Copies, c.Name,
                c.ExternalCardId, c.Categories.ToArray(), c.ManualMetadataCategoryKeys.ToArray())).ToArray(),
            combos.Where(c => c.Active).Select(c => new WorkerCombo(c.Name, c.GroupId,
                c.Categories.ToArray(), c.Cards.ToArray())).ToArray(), handSize, groups.ToArray()),
            CalculationJsonContext.Default.CalculationInput));
}

public sealed record WorkerCard(string Id, int Copies, string? Name, int? ExternalCardId,
    CategoryBase[] Categories, string[] ManualMetadataCategoryKeys);
public sealed record WorkerCombo(string? Name, string? GroupId, ComboCategory[] Categories, ComboCard[] Cards);
public sealed record CalculationInput(WorkerCard[] Cards, WorkerCombo[] Combos, int HandSize, ComboGroup[] Groups);
public sealed record CalculationResponse(ProbabilityCalculationResult? Result, string? Error, bool IsLimit = false);

public static class CalculationWire {
    public static string Execute(string json) {
        CalculationResponse response;
        try {
            var input = JsonSerializer.Deserialize(json, CalculationJsonContext.Default.CalculationInput)
                ?? throw new InvalidOperationException("Calculation input is missing.");
            var cards = input.Cards.Select(c => new Card(c.Categories, c.Copies, c.Name, true, c.Id,
                c.ExternalCardId, c.ManualMetadataCategoryKeys)).ToList();
            var combos = input.Combos.Select(c => new Combo(c.Categories, c.Name, true, c.GroupId, c.Cards)).ToList();
            response = new(new ProbabilityCalculatorService().CalculateProbabilityResults(
                cards, combos, input.HandSize, input.Groups), null);
        }
        catch (ProbabilityCalculationLimitException ex) { response = new(null, ex.Message, true); }
        catch (Exception ex) { response = new(null, ex.Message); }
        return JsonSerializer.Serialize(response, CalculationJsonContext.Default.CalculationResponse);
    }

    public static ProbabilityCalculationResult ReadResult(string json) {
        var response = JsonSerializer.Deserialize(json, CalculationJsonContext.Default.CalculationResponse)
            ?? throw new InvalidOperationException("Background calculation returned no response.");
        if (response.IsLimit) throw new ProbabilityCalculationLimitException();
        if (response.Error is not null) throw new InvalidOperationException(response.Error);
        return response.Result ?? throw new InvalidOperationException("Background calculation returned no result.");
    }
}

[JsonSerializable(typeof(CalculationInput))]
[JsonSerializable(typeof(CalculationResponse))]
internal partial class CalculationJsonContext : JsonSerializerContext;
