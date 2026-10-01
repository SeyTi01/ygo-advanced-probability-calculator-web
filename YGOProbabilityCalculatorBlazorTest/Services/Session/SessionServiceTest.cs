using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text;
using Microsoft.JSInterop;
using Moq;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.Session;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;
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
    public async Task LoadSessionAsync_HistoricalV1_2UnversionedFixture_MigratesAndPreservesLegacySession() {
        // Unchanged bundled Fiendsmith/Bystial example from the v1.2.0 release.
        var fileContent = await ReadSessionFixture("legacy_v1_2_example_session.json");
        using var document = JsonDocument.Parse(fileContent);
        var root = document.RootElement;
        Assert.That(root.TryGetProperty(nameof(SessionState.SchemaVersion), out _), Is.False);
        Assert.That(root.TryGetProperty("ComboGroups", out _), Is.False);
        Assert.That(root.TryGetProperty("CategoryColorIndices", out _), Is.False);
        foreach (var card in root.GetProperty("Cards").EnumerateArray()) {
            Assert.That(card.TryGetProperty("Id", out _), Is.False);
            Assert.That(card.TryGetProperty("Active", out _), Is.False);
        }
        foreach (var combo in root.GetProperty("Combos").EnumerateArray()) {
            Assert.That(combo.TryGetProperty("Cards", out _), Is.False);
            Assert.That(combo.TryGetProperty("Active", out _), Is.False);
            Assert.That(combo.TryGetProperty("GroupId", out _), Is.False);
        }

        var session = await CreateRealSerializerSessionService().LoadSessionAsync(fileContent);

        Assert.That(session.SchemaVersion, Is.EqualTo(SessionState.CurrentSchemaVersion));
        Assert.That(session.Categories.Select(category => category.Name), Is.EqualTo(new[] {
            "1 Card Starter", "Lubellion", "Normal Summon", "Bystial", "L/D Normal Summon"
        }));
        Assert.That(session.Cards.Select(card =>
            (card.Name, card.Copies, Categories: string.Join("|", card.Categories.Select(category => category.Name)))),
            Is.EqualTo(new[] {
                ("Fabled Lurrie", 1, "1 Card Starter"),
                ("Effect Veiler", 3, "Normal Summon|L/D Normal Summon"),
                ("Maxx \"C\"", 2, "Normal Summon"),
                ("Ash Blossom & Joyous Spring", 3, "Normal Summon"),
                ("Ghost Mourner & Moonlit Chill", 2, "Normal Summon"),
                ("Lacrima the Crimson Tears", 3, "1 Card Starter"),
                ("Artifact Lancea", 2, ""),
                ("Bystial Druiswurm", 2, "Bystial"),
                ("Bystial Magnamhut", 1, "Bystial"),
                ("Bystial Saronir", 1, "Bystial"),
                ("Bystial Baldrake", 2, "Bystial"),
                ("Fiendsmith Engraver", 2, "1 Card Starter"),
                ("Chaos Hunter", 3, ""),
                ("Fantastical Dragon Phantazmay", 3, ""),
                ("The Bystial Lubellion", 3, "Lubellion"),
                ("Nibiru, the Primal Being", 3, ""),
                ("Fiendsmith's Tract", 3, "1 Card Starter"),
                ("Branded Regained", 1, "")
            }));
        Assert.That(session.Combos.Select(combo => combo.Name),
            Is.EqualTo(new[] { "1-Card Combo", "Moon Combo", "Moon Combo 2" }));
        Assert.That(session.Combos.Select(combo => combo.Categories.Count), Is.EqualTo(new[] { 1, 2, 2 }));
        Assert.That(session.Combos.SelectMany(combo => combo.Categories.Select(category =>
            (combo.Name, category.BaseCategory.Name, category.MinCount, category.MaxCount))),
            Is.EqualTo(new[] {
                ("1-Card Combo", "1 Card Starter", 1, 5),
                ("Moon Combo", "Lubellion", 1, 5),
                ("Moon Combo", "Normal Summon", 1, 5),
                ("Moon Combo 2", "Bystial", 1, 5),
                ("Moon Combo 2", "L/D Normal Summon", 1, 5)
            }));
        Assert.That(session.HandSize, Is.EqualTo(5));

        Assert.That(session.Cards.All(card => card.Active), Is.True);
        Assert.That(session.Combos.All(combo => combo.Active), Is.True);
        Assert.That(session.Combos.All(combo => combo.Cards.Count == 0 && combo.GroupId == null), Is.True);
        Assert.That(session.ComboGroups, Is.Empty);
        Assert.That(session.CategoryColorIndices, Is.Empty);
        Assert.That(session.Cards.All(card => Guid.TryParseExact(card.Id, "N", out var id) && id != Guid.Empty), Is.True);
        Assert.That(session.Cards.Select(card => card.Id).Distinct(StringComparer.Ordinal).Count(),
            Is.EqualTo(session.Cards.Count));
    }

    [Test]
    public async Task LoadSessionAsync_CurrentEraUnversionedFixture_PreservesModernSessionData() {
        var fileContent = await ReadSessionFixture("vsmodel.json");
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
    public async Task LoadSessionAsync_CurrentEraUnversionedRepositoryFixturesStillLoad(string fixtureName) {
        var fileContent = await File.ReadAllTextAsync(
            Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", fixtureName));
        using var document = JsonDocument.Parse(fileContent);
        Assert.That(document.RootElement.TryGetProperty(nameof(SessionState.SchemaVersion), out _), Is.False);

        var session = await CreateRealSerializerSessionService().LoadSessionAsync(fileContent);

        Assert.That(session.SchemaVersion, Is.EqualTo(SessionState.CurrentSchemaVersion));
        Assert.That(session.Cards, Has.Count.EqualTo(25));
    }

    [TestCase("legacy_v1_2_example_session.json")]
    [TestCase("vsmodel.json")]
    public async Task SaveSessionAsync_WritesCurrentVersionAndRoundTripsThroughRealSerializer(string fixtureName) {
        var fileContent = await ReadSessionFixture(fixtureName);
        var loadService = CreateRealSerializerSessionService();
        var loadedSession = await loadService.LoadSessionAsync(fileContent);
        var sessionWithLegacyVersion = CopySession(loadedSession, schemaVersion: 0);
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
            Is.EqualTo(loadedSession.Categories.Select(category => category.Name)));
        Assert.That(reloaded.Cards.Select(card => (card.Id, card.Name, card.Copies, card.Active)),
            Is.EqualTo(loadedSession.Cards.Select(card => (card.Id, card.Name, card.Copies, card.Active))));
        Assert.That(reloaded.Combos.Select(combo => (combo.Name, combo.GroupId, combo.Active)),
            Is.EqualTo(loadedSession.Combos.Select(combo => (combo.Name, combo.GroupId, combo.Active))));
        Assert.That(reloaded.HandSize, Is.EqualTo(loadedSession.HandSize));
        Assert.That(reloaded.CategoryColorIndices, Is.EqualTo(loadedSession.CategoryColorIndices));
    }

    [Test]
    public async Task LoadSessionAsync_VersionOnePayload_MigratesAndPreservesData() {
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
        Assert.That(session.Categories.Concat(session.Cards.SelectMany(c => c.Categories))
            .Concat(session.Combos.SelectMany(c => c.Categories).Select(c => c.BaseCategory))
            .All(c => c.Source == CategorySource.User && c.MetadataKey is null), Is.True);
    }

    [Test]
    public async Task MetadataSessionRoundTripsWithoutExternalLookupOrTopLevelPropertyDefinitions() {
        var user = new CategoryBase("Spell");
        var property = new CategoryBase("Spell", CategorySource.Metadata, "kind:spell");
        var session = new SessionState {
            Categories = [user], Cards = [new([user, property], 2, "Quick spell", externalCardId: 123)],
            Combos = [new([new(user, 1, 2), new(property, 1, 2)], "Two copies")],
            HandSize = 2, CategoryColorIndices = new() { ["Spell"] = 7 }
        };
        var runtime = new CapturingJsRuntime();
        var service = new SessionService(runtime, new RealJsonSerializer());
        await service.SaveSessionAsync(session, "metadata");
        var loaded = await service.LoadSessionAsync(runtime.DownloadedJson);
        Assert.That(loaded.SchemaVersion, Is.EqualTo(2));
        Assert.That(loaded.Categories, Is.EqualTo(new[] { user }));
        Assert.That(loaded.Cards[0].Categories, Is.EqualTo(new[] { user, property }));
        Assert.That(loaded.Cards[0].ExternalCardId, Is.EqualTo(123));
        Assert.That(loaded.Cards[0].Id, Is.EqualTo(session.Cards[0].Id));
        Assert.That(loaded.Combos[0].Categories.Select(c => c.BaseCategory), Is.EqualTo(new[] { user, property }));
        Assert.That(loaded.CategoryColorIndices, Is.EqualTo(session.CategoryColorIndices));
        Assert.That(new ProbabilityCalculatorService().CalculateProbabilityForCombos(loaded.Cards, loaded.Combos, 2),
            Is.EqualTo(SmallDeckOracleTest.EnumerateProbability(session.Cards, session.Combos, 2)));
    }

    [TestCase(0)]
    [TestCase(1)]
    public void MigrationExplicitlyClassifiesAllPreV2CategoryLocations(int version) {
        var json = $$$"""
            { "SchemaVersion": {{{version}}}, "Categories": [{"Name":"Top", "Source":"Metadata", "MetadataKey":"spoof"}],
              "Cards": [{"Categories":[{"Name":"On card"}]}],
              "Combos": [{"Categories":[{"BaseCategory":{"Name":"On combo"}}]}] }
            """;
        var migrated = new SessionSchemaMigrator().MigrateToCurrent(json);
        using var document = JsonDocument.Parse(migrated);
        var root = document.RootElement;
        Assert.That(root.GetProperty("SchemaVersion").GetInt32(), Is.EqualTo(2));
        var locations = new[] { root.GetProperty("Categories")[0], root.GetProperty("Cards")[0].GetProperty("Categories")[0],
            root.GetProperty("Combos")[0].GetProperty("Categories")[0].GetProperty("BaseCategory") };
        foreach (var category in locations) {
            Assert.That(category.GetProperty("Source").GetString(), Is.EqualTo("User"));
            Assert.That(category.TryGetProperty("MetadataKey", out _), Is.False);
        }
        Assert.That(new SessionSchemaMigrator().MigrateToCurrent(migrated), Is.EqualTo(migrated));
    }

    [Test]
    public void LoadSessionAsync_FutureSchemaVersion_IsRejectedClearly() {
        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await CreateRealSerializerSessionService().LoadSessionAsync("{ \"SchemaVersion\": 3 }"));

        Assert.That(exception!.Message, Does.Contain("Unsupported session schema version 3"));
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
    public async Task LoadSessionAsync_CurrentEraUnversionedFixtureStillRejectsDuplicateCardIds() {
        var root = JsonNode.Parse(await ReadSessionFixture("vsmodel.json"))!.AsObject();
        var cards = root["Cards"]!.AsArray();
        cards.Add(cards[0]!.DeepClone());

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await CreateRealSerializerSessionService().LoadSessionAsync(root.ToJsonString()));

        Assert.That(exception!.Message, Does.Contain("duplicate card IDs"));
    }

    private static async Task<string> ReadSessionFixture(string fixtureName) => await File.ReadAllTextAsync(
        Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", fixtureName));

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
