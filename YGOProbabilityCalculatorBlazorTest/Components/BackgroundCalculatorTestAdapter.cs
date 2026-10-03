using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazorTest.Components;

// Keep existing editor tests and their adversarial synchronous fakes meaningful at the new boundary.
// Browser execution/termination is covered separately; this adapter exists only in the test assembly.
internal sealed class BackgroundCalculatorTestAdapter(IProbabilityCalculatorService engine) : IBackgroundCalculator {
    public Task<ProbabilityCalculationResult> CalculateAsync(CalculationSnapshot snapshot, CancellationToken token) {
        var input = JsonSerializer.Deserialize<CalculationInput>(snapshot.Json)!;
        return Task.Run(() => engine.CalculateProbabilityResults(
            input.Cards.Select(c => new Card(c.Categories, c.Copies, c.Name, true, c.Id,
                c.ExternalCardId, c.ManualMetadataCategoryKeys)).ToList(),
            input.Combos.Select(c => new Combo(c.Categories, c.Name, true, c.GroupId, c.Cards)).ToList(),
            input.HandSize, input.Groups));
    }
}
