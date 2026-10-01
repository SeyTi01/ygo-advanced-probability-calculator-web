using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text;
using Microsoft.JSInterop;
using Moq;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.Session;
using RealJsonSerializer = YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer;

namespace YGOProbabilityCalculatorBlazorTest.Services.Session;

[TestFixture]
public class SessionServiceTests {
    private Mock<IJSRuntime> _jsRuntimeMock;
    private Mock<ISerializer> _serializerMock;
    private SessionService _sessionService;

    [SetUp]
    public void Setup() {
        _jsRuntimeMock = new Mock<IJSRuntime>();
        _serializerMock = new Mock<ISerializer>();
        _sessionService = new SessionService(_jsRuntimeMock.Object, _serializerMock.Object);
    }

    [Test]
    public async Task SaveSessionAsync_WithValidFileName_CallsSerializerAndJsRuntime() {
        var session = new SessionState();
        const string fileName = "test";
        const string serializedJson = "{}";
        const string expectedFileName = "test.json";
        var expectedBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(serializedJson));

        _serializerMock.Setup(x => x.Serialize(It.IsAny<SessionState>(), It.IsAny<JsonSerializerOptions>()))
            .Returns(serializedJson);

        await _sessionService.SaveSessionAsync(session, fileName);

        _serializerMock.Verify(x => x.Serialize(
            It.Is<SessionState>(saved => saved.SchemaVersion == SessionState.CurrentSchemaVersion),
            It.IsAny<JsonSerializerOptions>()), Times.Once);
        _jsRuntimeMock.Verify(x => x.InvokeAsync<object>(
            "downloadFileFromStream",
            It.Is<object[]>(args =>
                args.Length == 2 &&
                args[0].ToString() == expectedFileName &&
                args[1].ToString() == expectedBase64
            )
        ), Times.Once);
    }


    [Test]
    public async Task SaveSessionAsync_WithJsonExtension_DoesNotAppendExtension() {
        var session = new SessionState();
        const string fileName = "test.json";
        const string serializedJson = "{}";
        var expectedBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(serializedJson));

        _serializerMock.Setup(x => x.Serialize(It.IsAny<SessionState>(), It.IsAny<JsonSerializerOptions>()))
            .Returns(serializedJson);

        await _sessionService.SaveSessionAsync(session, fileName);

        _jsRuntimeMock.Verify(x => x.InvokeAsync<object>(
            "downloadFileFromStream",
            It.Is<object[]>(args =>
                args.Length == 2 &&
                args[0].ToString() == fileName &&
                args[1].ToString() == expectedBase64
            )
        ), Times.Once);
    }

    [Test]
    public async Task LoadSessionAsync_WithValidJson_ReturnsDeserializedSession() {
        const string fileContent = "{}";
        var expectedSession = new SessionState();

        _serializerMock.Setup(x => x.Deserialize<SessionState>(It.IsAny<string>(), It.IsAny<JsonSerializerOptions>()))
            .Returns(expectedSession);

        var result = await _sessionService.LoadSessionAsync(fileContent);

        Assert.That(result, Is.SameAs(expectedSession));
        _serializerMock.Verify(x => x.Deserialize<SessionState>(
            It.Is<string>(json => JsonDocument.Parse(json).RootElement.GetProperty("SchemaVersion").GetInt32() == SessionState.CurrentSchemaVersion),
            It.IsAny<JsonSerializerOptions>()
        ), Times.Once);
    }

    [Test]
    public void LoadSessionAsync_WhenDeserializerReturnsNull_ThrowsInvalidOperationException() {
        const string fileContent = "{}";
        _serializerMock.Setup(x => x.Deserialize<SessionState>(It.IsAny<string>(), It.IsAny<JsonSerializerOptions>()))
            .Returns((SessionState?)null);

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _sessionService.LoadSessionAsync(fileContent));
        Assert.That(exception.Message, Is.EqualTo("Failed to deserialize session data"));
    }

    [Test]
    public void LoadSessionAsync_WhenDeserializerThrowsJsonException_ThrowsInvalidOperationException() {
        const string fileContent = "{ \"Cards\": [] }";
        _serializerMock.Setup(x => x.Deserialize<SessionState>(It.IsAny<string>(), It.IsAny<JsonSerializerOptions>()))
            .Throws(new JsonException("Invalid JSON"));

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _sessionService.LoadSessionAsync(fileContent));
        Assert.That(exception.Message, Is.EqualTo("Invalid session file format"));
    }

    [Test]
    public void LoadSessionAsync_RejectsDuplicateCardIds() {
        var original = new Card([], 1, "First");
        var duplicate = new Card([], 1, "Second", id: original.Id);
        var session = new SessionState { Cards = [original, duplicate] };
        _serializerMock.Setup(x => x.Deserialize<SessionState>(It.IsAny<string>(), It.IsAny<JsonSerializerOptions>()))
            .Returns(session);
        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _sessionService.LoadSessionAsync("{}"));
        Assert.That(exception!.Message, Does.Contain("duplicate card IDs"));
    }

    [Test]
    public async Task LoadSessionAsync_UnversionedPersistedFixture_MigratesAndPreservesSessionData() {
        var fileContent = await ReadPersistedSessionFixture();
        var root = JsonNode.Parse(fileContent)!.AsObject();
        Assert.That(root.ContainsKey(nameof(SessionState.SchemaVersion)), Is.False);

        // Exercise inactive state as well as the active values in the real persisted fixture.
        root["Cards"]![1]!["Active"] = false;
        root["Combos"]![0]!["Active"] = false;

        var service = CreateRealSerializerSessionService();
        var session = await service.LoadSessionAsync(root.ToJsonString());

        Assert.That(session.SchemaVersion, Is.EqualTo(SessionState.CurrentSchemaVersion));
        Assert.That(session.Categories.Select(category => category.Name), Is.EqualTo(new[] { "Fire", "Dark", "Earth", "Level 5" }));
        Assert.That(session.Cards, Has.Count.EqualTo(25));
        Assert.That(session.Cards[0].Id, Is.EqualTo("6791c7d39e9d41cda86fa8f59b6092a1"));
        Assert.That(session.Cards[0].Name, Is.EqualTo("Maxx \"C\""));
        Assert.That(session.Cards[0].Copies, Is.EqualTo(1));
        Assert.That(session.Cards[1].Active, Is.False);
        Assert.That(session.Cards[5].Categories.Select(category => category.Name), Is.EqualTo(new[] { "Fire", "Level 5" }));
        Assert.That(session.Cards[^1].Name, Is.EqualTo("Vanquish Soul Snow Devil"));
        Assert.That(session.Combos, Has.Count.EqualTo(10));
        Assert.That(session.Combos[0].Name, Is.EqualTo("Razen + Fire"));
        Assert.That(session.Combos[0].Categories[0].BaseCategory.Name, Is.EqualTo("Fire"));
        Assert.That(session.Combos[0].Categories[0].MinCount, Is.EqualTo(1));
        Assert.That(session.Combos[0].Categories[0].MaxCount, Is.EqualTo(5));
        Assert.That(session.Combos[0].Cards[0].CardId, Is.EqualTo(session.Cards[3].Id));
        Assert.That(session.Combos[0].Cards[0].MinCount, Is.EqualTo(1));
        Assert.That(session.Combos[0].Cards[0].MaxCount, Is.EqualTo(5));
        Assert.That(session.Combos[0].GroupId, Is.EqualTo("07a486fd83234c9ca02e7369c9b61c1b"));
        Assert.That(session.Combos[0].Active, Is.False);
        Assert.That(session.ComboGroups.Select(group => group.Name), Is.EqualTo(new[] { "Full VS", "Full K9" }));
        Assert.That(session.HandSize, Is.EqualTo(5));
        Assert.That(session.CategoryColorIndices, Is.EqualTo(new Dictionary<string, int> {
            ["Fire"] = 0,
            ["Dark"] = 1,
            ["Earth"] = 2,
            ["Level 5"] = 3
        }));
    }

    [TestCase("razen_session.json")]
    [TestCase("vsmodel.json")]
    public async Task LoadSessionAsync_ExistingUnversionedRepositoryFixturesStillLoad(string fixtureName) {
        var fileContent = await File.ReadAllTextAsync(
            Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", fixtureName));
        using var document = JsonDocument.Parse(fileContent);
        Assert.That(document.RootElement.TryGetProperty(nameof(SessionState.SchemaVersion), out _), Is.False);

        var session = await CreateRealSerializerSessionService().LoadSessionAsync(fileContent);

        Assert.That(session.SchemaVersion, Is.EqualTo(SessionState.CurrentSchemaVersion));
        Assert.That(session.Cards, Has.Count.EqualTo(25));
    }

    [Test]
    public async Task SaveSessionAsync_WritesCurrentVersionAndRoundTripsThroughRealSerializer() {
        var fileContent = await ReadPersistedSessionFixture();
        var loadService = CreateRealSerializerSessionService();
        var loadedLegacySession = await loadService.LoadSessionAsync(fileContent);
        var sessionWithLegacyVersion = CopySession(loadedLegacySession, schemaVersion: 0);
        var jsRuntime = new CapturingJsRuntime();
        var saveService = new SessionService(jsRuntime, new RealJsonSerializer());

        await saveService.SaveSessionAsync(sessionWithLegacyVersion, "round-trip");

        var savedJson = jsRuntime.DownloadedJson;
        using var savedDocument = JsonDocument.Parse(savedJson);
        Assert.That(savedDocument.RootElement.GetProperty(nameof(SessionState.SchemaVersion)).GetInt32(),
            Is.EqualTo(SessionState.CurrentSchemaVersion));
        Assert.That(sessionWithLegacyVersion.SchemaVersion, Is.EqualTo(0), "Saving should not mutate the caller's session state.");

        var reloaded = await loadService.LoadSessionAsync(savedJson);
        Assert.That(reloaded.SchemaVersion, Is.EqualTo(SessionState.CurrentSchemaVersion));
        Assert.That(reloaded.Categories.Select(category => category.Name),
            Is.EqualTo(loadedLegacySession.Categories.Select(category => category.Name)));
        Assert.That(reloaded.Cards.Select(card => (card.Id, card.Name, card.Copies, card.Active)),
            Is.EqualTo(loadedLegacySession.Cards.Select(card => (card.Id, card.Name, card.Copies, card.Active))));
        Assert.That(reloaded.Combos.Select(combo => (combo.Name, combo.GroupId, combo.Active)),
            Is.EqualTo(loadedLegacySession.Combos.Select(combo => (combo.Name, combo.GroupId, combo.Active))));
        Assert.That(reloaded.HandSize, Is.EqualTo(loadedLegacySession.HandSize));
        Assert.That(reloaded.CategoryColorIndices, Is.EqualTo(loadedLegacySession.CategoryColorIndices));
    }

    [Test]
    public async Task LoadSessionAsync_CurrentVersionPayload_PreservesData() {
        const string currentSessionJson = """
            {
              "SchemaVersion": 1,
              "Categories": [{ "Name": "Fire" }],
              "Cards": [{ "Categories": [{ "Name": "Fire" }], "Copies": 2, "Name": "Razen", "Active": false, "Id": "razen-id" }],
              "Combos": [{ "Categories": [{ "BaseCategory": { "Name": "Fire" }, "MinCount": 1, "MaxCount": 2 }], "Cards": [{ "CardId": "razen-id", "MinCount": 1, "MaxCount": 1 }], "Name": "Starter", "Active": false, "GroupId": "group-id" }],
              "ComboGroups": [{ "Id": "group-id", "Name": "Main line" }],
              "HandSize": 5,
              "CategoryColorIndices": { "Fire": 3 }
            }
            """;

        var service = CreateRealSerializerSessionService();
        var session = await service.LoadSessionAsync(currentSessionJson);

        Assert.That(session.SchemaVersion, Is.EqualTo(SessionState.CurrentSchemaVersion));
        Assert.That(session.Cards[0].Active, Is.False);
        Assert.That(session.Cards[0].Copies, Is.EqualTo(2));
        Assert.That(session.Combos[0].Cards[0].CardId, Is.EqualTo("razen-id"));
        Assert.That(session.Combos[0].Active, Is.False);
        Assert.That(session.Combos[0].GroupId, Is.EqualTo("group-id"));
        Assert.That(session.ComboGroups[0].Name, Is.EqualTo("Main line"));
        Assert.That(session.CategoryColorIndices["Fire"], Is.EqualTo(3));
    }

    [Test]
    public void LoadSessionAsync_FutureSchemaVersion_IsRejectedClearly() {
        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await CreateRealSerializerSessionService().LoadSessionAsync("{ \"SchemaVersion\": 2 }"));

        Assert.That(exception!.Message, Does.Contain("Unsupported session schema version 2"));
    }

    [TestCase("{ \"SchemaVersion\": \"1\" }")]
    [TestCase("{ \"SchemaVersion\": 1.5 }")]
    [TestCase("{ \"SchemaVersion\": -1 }")]
    [TestCase("{ \"SchemaVersion\": null }")]
    [TestCase("{ \"SchemaVersion\": true }")]
    [TestCase("{ \"SchemaVersion\": 1, \"schemaversion\": 1 }")]
    [TestCase("[]")]
    [TestCase("null")]
    [TestCase("invalid json")]
    public void LoadSessionAsync_MalformedSchemaOrRoot_IsRejectedAsInvalidSession(string fileContent) {
        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await CreateRealSerializerSessionService().LoadSessionAsync(fileContent));

        Assert.That(exception!.Message, Is.EqualTo("Invalid session file format"));
    }

    [Test]
    public async Task LoadSessionAsync_UnversionedFixtureStillRejectsDuplicateCardIds() {
        var root = JsonNode.Parse(await ReadPersistedSessionFixture())!.AsObject();
        var cards = root["Cards"]!.AsArray();
        cards.Add(cards[0]!.DeepClone());

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await CreateRealSerializerSessionService().LoadSessionAsync(root.ToJsonString()));

        Assert.That(exception!.Message, Does.Contain("duplicate card IDs"));
    }

    private static async Task<string> ReadPersistedSessionFixture() => await File.ReadAllTextAsync(
        Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "vsmodel.json"));

    private SessionService CreateRealSerializerSessionService() => new(_jsRuntimeMock.Object, new RealJsonSerializer());

    private static SessionState CopySession(SessionState session, int schemaVersion) => new() {
        SchemaVersion = schemaVersion,
        Categories = session.Categories,
        Cards = session.Cards,
        Combos = session.Combos,
        ComboGroups = session.ComboGroups,
        HandSize = session.HandSize,
        CategoryColorIndices = session.CategoryColorIndices
    };

    private sealed class CapturingJsRuntime : IJSRuntime {
        private object?[]? _lastArguments;

        public string DownloadedJson => Encoding.UTF8.GetString(Convert.FromBase64String((string)_lastArguments![1]!));

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) {
            _lastArguments = args;
            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
    }
}
