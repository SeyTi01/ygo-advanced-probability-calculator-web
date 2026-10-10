using System.Text.Json;
using Microsoft.JSInterop;
using Moq;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Converter;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.Session;
using YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.Session;

[TestFixture]
public class DrawEffectsSessionTest {
    private static SessionService Codec() => new(Mock.Of<IJSRuntime>(), new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public async Task SessionShareAndWorkerPreserveEffectsAndExactMeaning(int drawCount) {
        SessionState session = new() {
            Cards = [new([], 2, "Effect", id: "effect", drawCount: drawCount), new([], 4, "Ordinary", id: "ordinary")],
            Combos = [new([], "Retained", groupId: "g", cards: [new("effect", 1, 2)])],
            ComboGroups = [new("g", "Group")], HandSize = 2
        };
        SessionService codec = Codec();
        string json = codec.SerializeSession(session);
        Assert.That(JsonDocument.Parse(json).RootElement.GetProperty("SchemaVersion").GetInt32(), Is.EqualTo(4));
        string link = SessionShareCodec.CreateLink("https://example.invalid/", json);
        SessionState loaded = await codec.LoadSessionAsync(SessionShareCodec.Decode(new Uri(link).Fragment));
        Assert.That(loaded.Cards[0].DrawCount, Is.EqualTo(drawCount));
        Assert.That(codec.SerializeSession(loaded), Is.EqualTo(json));
        CalculationSnapshot snapshot = CalculationSnapshot.Capture(loaded.Cards, loaded.Combos, loaded.HandSize, loaded.ComboGroups);
        loaded.Cards[0] = loaded.Cards[0].WithDrawCount(null);
        CalculationInput input = JsonSerializer.Deserialize<CalculationInput>(snapshot.Json)!;
        Assert.That(input.Cards[0].DrawCount, Is.EqualTo(drawCount));
        DrawSequenceOracle.Counts oracle = DrawSequenceOracle.Enumerate(session.Cards, 2, session.Combos, session.ComboGroups);
        ProbabilityCalculationResult result = CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json));
        double expected = (double)oracle.Wins[0] / (double)oracle.Total;
        Assert.That(result.TotalProbability, Is.EqualTo(expected).Within(1e-14));
        Assert.That(result.ComboProbabilities.Single().Probability, Is.EqualTo(expected).Within(1e-14));
        Assert.That(result.GroupProbabilities!.Single().Probability, Is.EqualTo(expected).Within(1e-14));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public async Task OlderSchemasPreserveOrdinaryCardMeaning(int version) {
        string json = $$"""{"SchemaVersion":{{version}},"Cards":[{"Categories":[],"Copies":2,"Name":"P","DrawCount":2}]}""";
        SessionState loaded = await Codec().LoadSessionAsync(json);
        Assert.That(loaded.SchemaVersion, Is.EqualTo(4));
        Assert.That(loaded.Cards.Single().DrawCount, Is.Null);
    }

    [TestCase("0")]
    [TestCase("4")]
    [TestCase("-1")]
    [TestCase("1.5")]
    [TestCase("\"2\"")]
    [TestCase("true")]
    [TestCase("[]")]
    [TestCase("2147483648")]
    public void InvalidEffectValuesFailBeforeSessionReplacement(string value) {
        string json = "{\"SchemaVersion\":4,\"Cards\":[{\"Categories\":[],\"Copies\":1,\"Name\":null,\"DrawCount\":" + value + "}]}";
        Assert.ThrowsAsync<InvalidOperationException>(() => Codec().LoadSessionAsync(json));
    }

    [Test]
    public void DuplicateDrawFieldsAreRejectedAndNullIsOrdinary() {
        JsonSerializerOptions options = new() { Converters = { new CardConverter() } };
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Card>(
            "{\"Categories\":[],\"Copies\":1,\"Name\":null,\"DrawCount\":1,\"drawcount\":2}", options));
        Card? card = JsonSerializer.Deserialize<Card>("{\"Categories\":[],\"Copies\":1,\"Name\":null,\"DrawCount\":null}", options);
        Assert.That(card!.DrawCount, Is.Null);
    }

    [Test]
    public void EveryReplacementAndMetadataUpdatePreservesConfigurationAndIdentity() {
        CategoryBase fire = new("Fire", CategorySource.Metadata, "attribute:fire");
        Card card = new([], 2, "Card", id: "stable", drawCount: 2);
        Card manual = card.WithManualMetadataCategory(fire);
        Card[] replacements = [card.WithName("Renamed"), card.WithCopies(3), card.WithActive(false),
            card.WithCategories([new("Role")]), manual, manual.WithoutManualMetadataCategory(fire.MetadataKey!),
            manual.WithObjectiveMetadata([fire], 123)];
        foreach (Card replacement in replacements) {
            Assert.That(replacement.DrawCount, Is.EqualTo(2));
            Assert.That(replacement.Id, Is.EqualTo("stable"));
        }

        Assert.That(card.WithDrawCount(null).Copies, Is.EqualTo(2));
        Assert.That(card.WithDrawCount(null).Id, Is.EqualTo("stable"));
        Assert.That(new Card([]).DrawCount, Is.Null);
        Assert.Throws<ArgumentOutOfRangeException>(() => card.WithDrawCount(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => card.WithDrawCount(4));
    }

    [Test]
    public async Task EnrichmentPreservesExplicitDrawBehavior() {
        Mock<ICardInfoService> info = new();
        info.Setup(service => service.GetCardInfoByExactNamesAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new Dictionary<string, CardInfo> { ["Card"] = new() { Id = 123, Name = "Card", Type = "Spell Card" } });
        SessionState session = new() { Cards = [new([], name: "Card", id: "stable", drawCount: 3)] };
        await new LegacyCardMetadataEnricher(info.Object).EnrichAsync(session);
        Assert.That(session.Cards[0].ExternalCardId, Is.EqualTo(123));
        Assert.That(session.Cards[0].DrawCount, Is.EqualTo(3));
    }

    [Test]
    public void PinnedDeckSignatureChangesWhenOnlyDrawBehaviorChanges() {
        Card card = new([], 2, id: "stable");
        PinnedCalculationContext first = PinnedCalculationContext.Capture(0, 1, [card], [], [], _ => Guid.Empty, _ => Guid.Empty);
        PinnedCalculationContext second = PinnedCalculationContext.Capture(0, 1, [card.WithDrawCount(2)], [], [], _ => Guid.Empty, _ => Guid.Empty);
        Assert.That(second.DeckDefinition, Is.Not.EqualTo(first.DeckDefinition));
    }

    [Test]
    public void InvalidWorkerEffectIsAnInputFailureAndLimitsRemainTyped() {
        CalculationSnapshot snapshot = CalculationSnapshot.Capture([new([], 3, drawCount: 2)], [new([])], 1, [], new(1));
        Assert.Throws<ProbabilityCalculationLimitException>(() => CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json)));
        string invalid = snapshot.Json.Replace("\"DrawCount\":2", "\"DrawCount\":4");
        Assert.Throws<ArgumentException>(() => CalculationWire.ReadResult(CalculationWire.Execute(invalid)));
    }
}
