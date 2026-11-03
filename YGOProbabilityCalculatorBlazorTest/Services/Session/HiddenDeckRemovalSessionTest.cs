using YGOProbabilityCalculatorBlazorTest.Services.CardEffects;
using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.DeckImport;
using YGOProbabilityCalculatorBlazor.Services.Session;
using YGOProbabilityCalculatorBlazorTest.Fixtures;

namespace YGOProbabilityCalculatorBlazorTest.Services.Session;

public class HiddenDeckRemovalSessionTest {
    [Test]
    public async Task PresetSaveShareAndWorkerPreserveHiddenCost() {
        CardInfo info = new() { Id = 35261759, Name = "Pot of Desires" };
        Card pot = CuratedDrawEffectPresets.CreateImportedCard(info, 3, info.Id);
        Assert.That(pot.PreDrawDeckRemovalCount, Is.EqualTo(10));
        Assert.That(pot.DrawCount, Is.EqualTo(2));
        Assert.That(pot.DrawOncePerTurn, Is.True);
        SessionState session = new() { Cards = [pot, new([], 37, id: "filler")], Combos = [new([])], HandSize = 5 };
        string json = SessionFixture.CreateCodec().SerializeSession(session);
        string fragment = new Uri(SessionShareCodec.CreateLink("https://example.invalid/", json)).Fragment;
        SessionState loaded = await SessionFixture.CreateCodec().LoadSessionAsync(SessionShareCodec.Decode(fragment));
        Assert.That(loaded.SchemaVersion, Is.EqualTo(SessionState.CurrentSchemaVersion));
        Assert.That(loaded.Cards[0].PreDrawDeckRemovalCount, Is.EqualTo(10));
        Assert.That(SessionFixture.CreateCodec().SerializeSession(loaded), Is.EqualTo(json));
        CalculationSnapshot snapshot = CalculationSnapshot.Capture(loaded.Cards, loaded.Combos, 5, [], options: new());
        Assert.That(JsonSerializer.Deserialize<CalculationInput>(snapshot.Json)!.Cards[0].PreDrawDeckRemovalCount, Is.EqualTo(10));
        Assert.That(CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json)).TotalProbability, Is.EqualTo(1));
        Card custom = pot.WithPreDrawDeckRemovalCount(3);
        Assert.That(CuratedDrawEffectPresets.ApplyForExplicitSelection(custom, custom, info).PreDrawDeckRemovalCount, Is.EqualTo(3));
        Assert.That(CuratedDrawEffectPresets.ApplyForExplicitSelection(custom, custom, new() { Id = 70368879 }).PreDrawDeckRemovalCount, Is.Zero);
        Assert.That(CuratedDrawEffectPresets.ApplyForExplicitSelection(new([], id: "x"), new([], id: "x"), info).PreDrawDeckRemovalCount, Is.EqualTo(10));
    }

    [TestCase(0), TestCase(1), TestCase(2), TestCase(3), TestCase(4), TestCase(5)]
    public async Task AbsentFieldDefaultsToZeroAndReleasedExtensionsStayIgnored(int schema) {
        string json = $$"""{"SchemaVersion":{{schema}},"Cards":[{"Categories":[],"Copies":1,"Name":"Pot of Desires","ExternalCardId":35261759,"DrawCount":2}]}""";
        Assert.That((await SessionFixture.CreateCodec().LoadSessionAsync(json)).Cards.Single().PreDrawDeckRemovalCount, Is.Zero);
        if (schema < 5) {
            json = json.Replace("\"DrawCount\":2", "\"DrawCount\":2,\"PreDrawDeckRemovalCount\":10");
            Assert.That((await SessionFixture.CreateCodec().LoadSessionAsync(json)).Cards.Single().PreDrawDeckRemovalCount, Is.Zero);
        }
    }

    [TestCase("null"), TestCase("-1"), TestCase("61"), TestCase("1.5"), TestCase("\"1\"")]
    [TestCase("1,\"predrawdeckremovalcount\":1")]
    public void InvalidOrDuplicatedDeckCostsFailClosed(string value) {
        string json = "{\"SchemaVersion\":5,\"Cards\":[{\"Categories\":[],\"Copies\":1,\"Name\":null,\"DrawCount\":2,\"PreDrawDeckRemovalCount\":" + value + "}]}";
        Assert.ThrowsAsync<InvalidOperationException>(() => SessionFixture.CreateCodec().LoadSessionAsync(json));
    }
}
