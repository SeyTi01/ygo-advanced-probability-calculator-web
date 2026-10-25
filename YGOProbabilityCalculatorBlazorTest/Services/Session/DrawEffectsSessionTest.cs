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

    [TestCase(1, true)]
    [TestCase(2, true)]
    [TestCase(3, true)]
    [TestCase(1, false)]
    [TestCase(2, false)]
    [TestCase(3, false)]
    [TestCase(1, false, 1)]
    [TestCase(3, false, 2)]
    [TestCase(2, true, 1)]
    [TestCase(1, true, 2)]
    public async Task SessionShareAndWorkerPreserveEffectsAndExactMeaning(int drawCount, bool oncePerTurn, int discard = 0) {
        SessionState session = new() {
            Cards = [new([], 2, "Effect", id: "effect", drawCount: drawCount, drawOncePerTurn: oncePerTurn, postDrawDiscardCount: discard), new([], 4, "Ordinary", id: "ordinary")],
            Combos = [new([], "Retained", groupId: "g", cards: [new("effect", 1, 2)])],
            ComboGroups = [new("g", "Group")], HandSize = 2
        };
        SessionService codec = Codec();
        string json = codec.SerializeSession(session);
        Assert.That(JsonDocument.Parse(json).RootElement.GetProperty("SchemaVersion").GetInt32(), Is.EqualTo(SessionState.CurrentSchemaVersion));
        string link = SessionShareCodec.CreateLink("https://example.invalid/", json);
        SessionState loaded = await codec.LoadSessionAsync(SessionShareCodec.Decode(new Uri(link).Fragment));
        Assert.That(loaded.Cards[0].DrawCount, Is.EqualTo(drawCount));
        Assert.That(loaded.Cards[0].DrawOncePerTurn, Is.EqualTo(oncePerTurn));
        Assert.That(loaded.Cards[0].PostDrawDiscardCount, Is.EqualTo(discard));
        Assert.That(codec.SerializeSession(loaded), Is.EqualTo(json));
        CalculationSnapshot snapshot = CalculationSnapshot.Capture(loaded.Cards, loaded.Combos, loaded.HandSize, loaded.ComboGroups,
            options: new());
        loaded.Cards[0] = loaded.Cards[0].WithDrawCount(null);
        CalculationInput input = JsonSerializer.Deserialize<CalculationInput>(snapshot.Json)!;
        Assert.That(input.Cards[0].DrawCount, Is.EqualTo(drawCount));
        Assert.That(input.Cards[0].DrawOncePerTurn, Is.EqualTo(oncePerTurn));
        Assert.That(input.Cards[0].PostDrawDiscardCount, Is.EqualTo(discard));
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
        string json = $$"""{"SchemaVersion":{{version}},"DrawEffectsEnabled":false,"Cards":[{"Categories":[],"Copies":2,"Name":"P","DrawCount":2,"DrawOncePerTurn":false,"PostDrawDiscardCount":1}]}""";
        SessionState loaded = await Codec().LoadSessionAsync(json);
        Assert.That(loaded.SchemaVersion, Is.EqualTo(SessionState.CurrentSchemaVersion));
        Assert.That(loaded.CalculationModel, Is.EqualTo(CalculationModel.OpeningHand));
        Assert.That(loaded.Cards.Single().DrawCount, Is.Null);
        Assert.That(loaded.Cards.Single().DrawOncePerTurn, Is.True);
        Assert.That(loaded.Cards.Single().PostDrawDiscardCount, Is.Zero);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task GlobalDrawEffectsSettingRoundTripsWithoutChangingCardConfiguration(bool enabled) {
        SessionService codec = Codec();
        Card configured = new([], 2, "Configured", id: "configured", drawCount: 3, drawOncePerTurn: false);
        SessionState session = new() { CalculationModel = (enabled ? CalculationModel.Fast : CalculationModel.OpeningHand), Cards = [configured] };

        Assert.That(new SessionState().CalculationModel, Is.EqualTo(CalculationModel.Fast));
        Assert.That(SessionState.CurrentSchemaVersion, Is.EqualTo(13));
        string json = codec.SerializeSession(session);
        using JsonDocument document = JsonDocument.Parse(json);
        Assert.That(document.RootElement.GetProperty("SchemaVersion").GetInt32(), Is.EqualTo(SessionState.CurrentSchemaVersion));
        Assert.That(document.RootElement.GetProperty("CalculationModel").GetString(), Is.EqualTo(enabled ? "Fast" : "OpeningHand"));

        SessionState loaded = await codec.LoadSessionAsync(json);
        Assert.That(loaded.CalculationModel, Is.EqualTo(enabled ? CalculationModel.Fast : CalculationModel.OpeningHand));
        Assert.That((loaded.Cards.Single().DrawCount, loaded.Cards.Single().DrawOncePerTurn), Is.EqualTo(((int?)3, false)));
    }

    [Test]
    public async Task MissingGlobalDrawEffectsSettingInSchemaFourDefaultsEnabled() {
        SessionState loaded = await Codec().LoadSessionAsync(
            """{"SchemaVersion":4,"Cards":[{"Categories":[],"Copies":2,"Name":"P","DrawCount":2,"DrawOncePerTurn":false}]}""");

        Assert.That(loaded.CalculationModel, Is.EqualTo(CalculationModel.Fast));
        Assert.That((loaded.Cards.Single().DrawCount, loaded.Cards.Single().DrawOncePerTurn), Is.EqualTo(((int?)2, false)));
    }

    [Test]
    public async Task SavedCuratedCardWithoutDrawSettingsRemainsOrdinaryWhenLoaded() {
        SessionState loaded = await Codec().LoadSessionAsync(
            """{"SchemaVersion":4,"Cards":[{"Id":"saved","Categories":[],"Copies":2,"Name":"Pot of Greed","ExternalCardId":55144522}]}""");

        Assert.That((loaded.Cards.Single().ExternalCardId, loaded.Cards.Single().DrawCount, loaded.Cards.Single().DrawOncePerTurn),
            Is.EqualTo(((int?)55144522, (int?)null, true)));
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
        Card card = new([], 2, "Card", id: "stable", drawCount: 2, drawOncePerTurn: false, postDrawDiscardCount: 1);
        Card manual = card.WithManualMetadataCategory(fire);
        Card[] replacements = [card.WithName("Renamed"), card.WithCopies(3), card.WithActive(false),
            card.WithCategories([new("Role")]), manual, manual.WithoutManualMetadataCategory(fire.MetadataKey!),
            manual.WithObjectiveMetadata([fire], 123)];
        foreach (Card replacement in replacements) {
            Assert.That(replacement.DrawCount, Is.EqualTo(2));
            Assert.That(replacement.DrawOncePerTurn, Is.False);
            Assert.That(replacement.PostDrawDiscardCount, Is.EqualTo(1));
            Assert.That(replacement.Id, Is.EqualTo("stable"));
        }

        Assert.That(card.WithDrawCount(null).Copies, Is.EqualTo(2));
        Assert.That(card.WithDrawCount(null).PostDrawDiscardCount, Is.Zero);
        Assert.That(card.WithDrawCount(3).PostDrawDiscardCount, Is.EqualTo(1));
        Assert.That(card.WithDrawOncePerTurn(true).PostDrawDiscardCount, Is.EqualTo(1));
        Assert.That(card.WithDrawCount(null).Id, Is.EqualTo("stable"));
        Assert.That(card.WithDrawCount(null).WithDrawCount(3).DrawOncePerTurn, Is.False);
        Assert.That(card.WithDrawOncePerTurn(true).Id, Is.EqualTo("stable"));
        Assert.That(card.WithDrawOncePerTurn(true).DrawCount, Is.EqualTo(2));
        Assert.That(new Card([]).DrawCount, Is.Null);
        Assert.That(new Card([]).DrawOncePerTurn, Is.True);
        Assert.Throws<ArgumentOutOfRangeException>(() => card.WithDrawCount(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => card.WithDrawCount(4));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Card([], postDrawDiscardCount: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => card.WithPostDrawDiscardCount(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => card.WithPostDrawDiscardCount(3));
    }

    [Test]
    public async Task EnrichmentPreservesExplicitDrawBehavior() {
        Mock<ICardInfoService> info = new();
        info.Setup(service => service.GetCardInfoByExactNamesAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new Dictionary<string, CardInfo> { ["Card"] = new() { Id = 123, Name = "Card", Type = "Spell Card" } });
        SessionState session = new() { Cards = [new([], name: "Card", id: "stable", drawCount: 3, drawOncePerTurn: false)] };
        await new LegacyCardMetadataEnricher(info.Object).EnrichAsync(session);
        Assert.That(session.Cards[0].ExternalCardId, Is.EqualTo(123));
        Assert.That(session.Cards[0].DrawCount, Is.EqualTo(3));
        Assert.That(session.Cards[0].DrawOncePerTurn, Is.False);
    }

    [Test]
    public async Task OmittedLimitInExistingSchemaFourAndWorkerInputsRemainsEnabled() {
        SessionState loaded = await Codec().LoadSessionAsync("""{"SchemaVersion":4,"Cards":[{"Categories":[],"Copies":2,"Name":"P","DrawCount":2}]}""");
        Assert.That(loaded.Cards.Single().DrawOncePerTurn, Is.True);
        CalculationSnapshot snapshot = CalculationSnapshot.Capture(loaded.Cards, [new([])], 1, []);
        string legacyWire = snapshot.Json.Replace(",\"DrawOncePerTurn\":true", "");
        Assert.That(legacyWire, Does.Not.Contain("DrawOncePerTurn"));
        Assert.That(JsonSerializer.Deserialize<CalculationInput>(legacyWire)!.Cards.Single().DrawOncePerTurn, Is.True);
        Assert.That(CalculationWire.Execute(legacyWire), Is.EqualTo(CalculationWire.Execute(snapshot.Json)));

        loaded.Cards[0] = loaded.Cards[0].WithDrawOncePerTurn(false).WithDrawCount(null);
        SessionState ordinary = await Codec().LoadSessionAsync(Codec().SerializeSession(loaded));
        Assert.That(ordinary.Cards.Single().WithDrawCount(2).DrawOncePerTurn, Is.False);
    }

    [TestCase("null")]
    [TestCase("1")]
    [TestCase("\"false\"")]
    [TestCase("{}")]
    [TestCase("false,\"drawonceperturn\":true")]
    public void InvalidOrDuplicateOncePerTurnSettingIsRejected(string value) {
        string json = "{\"SchemaVersion\":4,\"Cards\":[{\"Categories\":[],\"Copies\":1,\"Name\":null,\"DrawOncePerTurn\":" + value + "}]}";
        Assert.ThrowsAsync<InvalidOperationException>(() => Codec().LoadSessionAsync(json));
    }

    [Test]
    public void InvalidWorkerEffectIsAnInputFailureAndLimitsRemainTyped() {
        CalculationSnapshot snapshot = CalculationSnapshot.Capture([new([], 3, drawCount: 2)], [new([])], 1, [], new(1));
        Assert.Throws<ProbabilityCalculationLimitException>(() => CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json)));
        string invalid = snapshot.Json.Replace("\"DrawCount\":2", "\"DrawCount\":4");
        Assert.Throws<ArgumentException>(() => CalculationWire.ReadResult(CalculationWire.Execute(invalid)));
    }

    [TestCase("null"), TestCase("-1"), TestCase("3"), TestCase("1.5"), TestCase("\"1\""), TestCase("true")]
    [TestCase("2147483648"), TestCase("[]"), TestCase("1,\"postdrawdiscardcount\":2")]
    public void PostDrawInvalidSessionValuesAreRejected(string value) {
        string json = "{\"SchemaVersion\":4,\"Cards\":[{\"Categories\":[],\"Copies\":1,\"Name\":null,\"DrawCount\":3,\"PostDrawDiscardCount\":" + value + "}]}";
        Assert.ThrowsAsync<InvalidOperationException>(() => Codec().LoadSessionAsync(json));
    }

    [Test]
    public void PostDrawWithoutDrawingIsRejectedInSessionAndWorkerInputs() {
        string json = """{"SchemaVersion":4,"Cards":[{"Categories":[],"Copies":1,"Name":null,"PostDrawDiscardCount":1}]}""";
        Assert.ThrowsAsync<InvalidOperationException>(() => Codec().LoadSessionAsync(json));
        CalculationSnapshot snapshot = CalculationSnapshot.Capture([new([], drawCount: 1, postDrawDiscardCount: 1)], [new([])], 1, []);
        string invalid = snapshot.Json.Replace("\"DrawCount\":1", "\"DrawCount\":null");
        Assert.Throws<ArgumentException>(() => CalculationWire.ReadResult(CalculationWire.Execute(invalid)));
    }

    [TestCase(74117290, 1), TestCase(79571449, 3)]
    public async Task PostDrawOldCuratedSessionsAndMetadataRemainAuthoritative(int externalId, int draw) {
        string json = $$"""{"SchemaVersion":4,"Cards":[{"Id":"saved","Categories":[],"Copies":1,"Name":"Saved","ExternalCardId":{{externalId}},"DrawCount":{{draw}}}]}""";
        SessionState loaded = await Codec().LoadSessionAsync(json);
        Assert.That(loaded.Cards.Single().PostDrawDiscardCount, Is.Zero);
        Assert.That(loaded.Cards.Single().WithObjectiveMetadata([], externalId).PostDrawDiscardCount, Is.Zero);
    }

    [Test]
    public void CustomPostDrawPlanIsIndependentOfBuiltInWorkerPlan() {
        List<Card> deck = [new([], 3, id: "source", drawCount: 3, postDrawDiscardCount: 2), new([], 10, id: "filler")];
        List<Combo> combos = [new([], cards: [new("filler", 5, 5)])];
        CalculationSnapshot disabled = CalculationSnapshot.Capture(deck.Select(card => card.WithPostDrawDiscardCount(0)), combos, 5, []);
        CalculationSnapshot ordinary = CalculationSnapshot.Capture(deck.Select(card => card.WithPostDrawDiscardCount(0)), combos, 5, []);
        Assert.That(CalculationWire.Execute(disabled.Json), Is.EqualTo(CalculationWire.Execute(ordinary.Json)));
        Assert.That(JsonSerializer.Deserialize<CalculationInput>(disabled.Json)!.Cards[0].PostDrawDiscardCount, Is.Zero);
        CalculationSnapshot enabled = CalculationSnapshot.Capture(deck, combos, 5, [], options: new());
        Assert.That(CalculationWire.Execute(enabled.Json), Is.Not.EqualTo(CalculationWire.Execute(disabled.Json)));
    }
}
