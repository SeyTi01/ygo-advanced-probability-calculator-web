using System.Text.Json;
using Microsoft.JSInterop;
using Moq;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Session;
using YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.Session;

[TestFixture]
public class SameCardSessionTest {
    private static SessionService Codec() => new(Mock.Of<IJSRuntime>(), new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());

    [Test]
    public async Task SameCardSessionShareWorkerAndPinnedSignaturesPreserveCompositeMeaning() {
        CategoryBase a = SameCardSelectorTest.A;
        CategoryBase b = SameCardSelectorTest.B;
        ComboCategory requirement = ComboCategory.SameCard([a, b], 1, 0, RequirementMaximumMode.HandSize);
        SessionState session = new() {
            Cards = [new([], id: "effect", drawCount: 2, postDrawDiscardCount: 1, postDrawSelector: SameCardSelectorTest.Composite),
                new([a, b], 2, id: "ab"), new([a], id: "a"), new([b], id: "b")],
            Combos = [new([requirement]), new([], alternativeGroups: [new([ComboAlternative.For(requirement), ComboAlternative.For(new ComboCard("a", 1, 1))])])],
            HandSize = 2
        };
        string json = Codec().SerializeSession(session);
        string fragment = new Uri(SessionShareCodec.CreateLink("https://example.invalid/", json)).Fragment;
        SessionState loaded = await Codec().LoadSessionAsync(SessionShareCodec.Decode(fragment));
        Assert.That(loaded.SchemaVersion, Is.EqualTo(5));
        Assert.That(Codec().SerializeSession(loaded), Is.EqualTo(json));
        Assert.That(loaded.Combos[0].Categories[0].Selector, Is.EqualTo(requirement.Selector));
        Assert.That(loaded.Combos[1].AlternativeGroups[0].Alternatives[0].Category!.Selector, Is.EqualTo(requirement.Selector));
        CalculationSnapshot snapshot = CalculationSnapshot.Capture(loaded.Cards, loaded.Combos, 2, [], options: new());
        Assert.That(JsonSerializer.Deserialize<CalculationInput>(snapshot.Json)!.Combos[0].Categories[0].Selector, Is.EqualTo(requirement.Selector));
        Assert.That(CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json)).TotalProbability, Is.InRange(0.0, 1.0));
        PinnedCalculationContext before = PinnedCalculationContext.Capture(0, 2, loaded.Cards, loaded.Combos, [], _ => Guid.Empty, _ => Guid.Empty);
        loaded.Combos[0] = new([new(a, 1, 0, RequirementMaximumMode.HandSize)]);
        PinnedCalculationContext after = PinnedCalculationContext.Capture(0, 2, loaded.Cards, loaded.Combos, [], _ => Guid.Empty, _ => Guid.Empty);
        Assert.That(before.Combos[0], Is.Not.EqualTo(after.Combos[0]));
    }

    [TestCase("{\"Kind\":\"AllOf\",\"Categories\":[]}", "[]")]
    [TestCase("{\"Kind\":\"AnyOf\",\"Categories\":[\"user:A\",\"user:B\"]}", "[{\"Name\":\"A\"},{\"Name\":\"B\"}]")]
    [TestCase("{\"Kind\":\"AllOf\",\"Categories\":[\"user:A\",\"user:B\"]}", "[{\"Name\":\"A\"}]")]
    public void SameCardSessionRejectsMalformedRequirement(string selector, string categories) {
        string json = $$"""{"SchemaVersion":4,"Combos":[{"Categories":[{"Selector":{{selector}},"CategoryBases":{{categories}},"MinCount":1,"MaxCount":2}]}]}""";
        Assert.ThrowsAsync<InvalidOperationException>(() => Codec().LoadSessionAsync(json));
    }
}
