using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazorTest.Components;

// Keep existing editor tests and their adversarial synchronous fakes meaningful at the new boundary.
// Browser execution/termination is covered separately; this adapter exists only in the test assembly.
internal sealed class BackgroundCalculatorTestAdapter(IProbabilityCalculatorService engine) : IBackgroundCalculator {
    public Task<ProbabilityCalculationResult> CalculateAsync(CalculationSnapshot snapshot, CancellationToken token) {
        CalculationInput input = JsonSerializer.Deserialize<CalculationInput>(snapshot.Json)!;
        return Task.Run(() => engine.CalculateProbabilityResults(
            input.Cards.Select(card => new Card(
                card.Categories,
                card.Copies,
                card.Name,
                true,
                card.Id,
                card.ExternalCardId,
                card.ManualMetadataCategoryKeys,
                card.DrawCount
            )).ToList(),
            input.Combos.Select(combo => new Combo(
                combo.Categories,
                combo.Name,
                true,
                combo.GroupId,
                combo.Cards
            )).ToList(),
            input.HandSize,
            input.Groups
        ));
    }
}
