using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.JSInterop;
using Moq;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.Session;
using YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;
using RealJsonSerializer = YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer;

namespace YGOProbabilityCalculatorBlazorTest.Services.Session;

[TestFixture]
public class SessionServiceTests
{
    private Mock<IJSRuntime> _jsRuntimeMock;
    private Mock<ISerializer> _serializerMock;
    private SessionService _sessionService;

    [SetUp]
    public void Setup()
    {
        _jsRuntimeMock = new Mock<IJSRuntime>();
        _serializerMock = new Mock<ISerializer>();
        _sessionService = new SessionService(_jsRuntimeMock.Object, _serializerMock.Object);
    }

    [Test]
    public async Task SaveSessionAsync_WithValidFileName_CallsSerializerAndJsRuntime()
    {
        SessionState session = new();
        const string fileName = "test";
        const string serializedJson = "{}";
        const string expectedFileName = "test.json";
        string expectedBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(serializedJson));

        _serializerMock
            .Setup(x => x.Serialize(It.IsAny<SessionState>(), It.IsAny<JsonSerializerOptions>()))
            .Returns(serializedJson);

        await _sessionService.SaveSessionAsync(session, fileName);

        _serializerMock.Verify(
            x =>
                x.Serialize(
                    It.Is<SessionState>(saved =>
                        saved.SchemaVersion == SessionState.CurrentSchemaVersion
                    ),
                    It.IsAny<JsonSerializerOptions>()
                ),
            Times.Once
        );
        _jsRuntimeMock.Verify(
            x =>
                x.InvokeAsync<object>(
                    "saveSessionFile",
                    It.Is<object[]>(args =>
                        args.Length == 2
                        && args[0].ToString() == expectedFileName
                        && args[1].ToString() == expectedBase64
                    )
                ),
            Times.Once
        );
    }

    [Test]
    public async Task SaveSessionAsync_WithJsonExtension_DoesNotAppendExtension()
    {
        SessionState session = new();
        const string fileName = "test.json";
        const string serializedJson = "{}";
        string expectedBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(serializedJson));

        _serializerMock
            .Setup(x => x.Serialize(It.IsAny<SessionState>(), It.IsAny<JsonSerializerOptions>()))
            .Returns(serializedJson);

        await _sessionService.SaveSessionAsync(session, fileName);

        _jsRuntimeMock.Verify(
            x =>
                x.InvokeAsync<object>(
                    "saveSessionFile",
                    It.Is<object[]>(args =>
                        args.Length == 2
                        && args[0].ToString() == fileName
                        && args[1].ToString() == expectedBase64
                    )
                ),
            Times.Once
        );
    }

    [Test]
    public async Task SaveSessionAsync_WhenInteropResolvesNormally_CompletesSuccessfully()
    {
        CapturingJsRuntime runtime = new();
        SessionService service = new(runtime, new RealJsonSerializer());

        await service.SaveSessionAsync(new SessionState(), "cancelled");

        Assert.That(runtime.InvokedFunction, Is.EqualTo("saveSessionFile"));
    }

    [Test]
    public void SaveSessionAsync_WhenJavaScriptInteropFails_PropagatesTheFailure()
    {
        SessionService service = new(new FailingJsRuntime(), new RealJsonSerializer());

        JSException? exception = Assert.ThrowsAsync<JSException>(async () =>
            await service.SaveSessionAsync(new SessionState(), "write-failure")
        );

        Assert.That(exception!.Message, Is.EqualTo("Session file write failed."));
    }

    [Test]
    public async Task LoadSessionAsync_WithValidJson_ReturnsDeserializedSession()
    {
        const string fileContent = "{}";
        SessionState expectedSession = new();

        _serializerMock
            .Setup(x =>
                x.Deserialize<SessionState>(It.IsAny<string>(), It.IsAny<JsonSerializerOptions>())
            )
            .Returns(expectedSession);

        SessionState result = await _sessionService.LoadSessionAsync(fileContent);

        Assert.That(result, Is.SameAs(expectedSession));
        _serializerMock.Verify(
            x =>
                x.Deserialize<SessionState>(
                    It.Is<string>(json =>
                        JsonDocument.Parse(json).RootElement.GetProperty("SchemaVersion").GetInt32()
                        == SessionState.CurrentSchemaVersion
                    ),
                    It.IsAny<JsonSerializerOptions>()
                ),
            Times.Once
        );
    }

    [Test]
    public void LoadSessionAsync_WhenDeserializerReturnsNull_ThrowsInvalidOperationException()
    {
        const string fileContent = "{}";
        _serializerMock
            .Setup(x =>
                x.Deserialize<SessionState>(It.IsAny<string>(), It.IsAny<JsonSerializerOptions>())
            )
            .Returns((SessionState?)null);

        InvalidOperationException? exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () =>
                await _sessionService.LoadSessionAsync(fileContent)
        );
        Assert.That(exception.Message, Is.EqualTo("Failed to deserialize session data"));
    }

    [Test]
    public void LoadSessionAsync_WhenDeserializerThrowsJsonException_ThrowsInvalidOperationException()
    {
        const string fileContent = "{ \"Cards\": [] }";
        _serializerMock
            .Setup(x =>
                x.Deserialize<SessionState>(It.IsAny<string>(), It.IsAny<JsonSerializerOptions>())
            )
            .Throws(new JsonException("Invalid JSON"));

        InvalidOperationException? exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () =>
                await _sessionService.LoadSessionAsync(fileContent)
        );
        Assert.That(exception.Message, Is.EqualTo("Invalid session file format"));
    }

    [Test]
    public void LoadSessionAsync_RejectsDuplicateCardIds()
    {
        Card original = new([], 1, "First");
        Card duplicate = new([], 1, "Second", id: original.Id);
        SessionState session = new() { Cards = [original, duplicate] };
        _serializerMock
            .Setup(x =>
                x.Deserialize<SessionState>(It.IsAny<string>(), It.IsAny<JsonSerializerOptions>())
            )
            .Returns(session);
        InvalidOperationException? exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () =>
                await _sessionService.LoadSessionAsync("{}")
        );
        Assert.That(exception!.Message, Does.Contain("duplicate card IDs"));
    }

    [TestCase("Categories", "null")]
    [TestCase("Cards", "null")]
    [TestCase("Combos", "null")]
    [TestCase("Categories", "[null]")]
    [TestCase("Cards", "[null]")]
    [TestCase("Combos", "[null]")]
    [TestCase("ComboGroups", "[null]")]
    public void LoadSessionAsync_RejectsMissingModelCollectionsAndEntries(
        string field,
        string value
    )
    {
        SessionService service = new(_jsRuntimeMock.Object, new RealJsonSerializer());
        string json = $"{{\"SchemaVersion\":3,\"{field}\":{value}}}";
        InvalidOperationException? exception = Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.LoadSessionAsync(json)
        );
        Assert.That(exception!.Message, Is.EqualTo("Invalid session file format"));
    }

    [Test]
    public async Task LoadSessionAsync_AbsentCollectionsAndLegacyNullOptionalFieldsRemainSupported()
    {
        SessionService service = new(_jsRuntimeMock.Object, new RealJsonSerializer());
        SessionState session = await service.LoadSessionAsync(
            "{\"ComboGroups\":null,\"CategoryColorIndices\":null}"
        );
        Assert.That(session.Categories, Is.Empty);
        Assert.That(session.Cards, Is.Empty);
        Assert.That(session.Combos, Is.Empty);
    }

    [TestCase("\"Cards\":[{\"Categories\":[null],\"Copies\":1,\"Name\":\"Card\"}]")]
    [TestCase("\"Combos\":[{\"Categories\":[null]}]")]
    [TestCase("\"Combos\":[{\"Categories\":[],\"Cards\":[null]}]")]
    [TestCase("\"Categories\":[{\"Name\":\"Role\"},{\"Name\":\"Role\"}]")]
    [TestCase("\"ComboGroups\":[{\"Id\":\"g\",\"Name\":\"One\"},{\"Id\":\"g\",\"Name\":\"Two\"}]")]
    [TestCase("\"ComboGroups\":[{\"Name\":\"Group\"}]")]
    [TestCase("\"ComboGroups\":[{\"Id\":null,\"Name\":\"Group\"}]")]
    [TestCase("\"ComboGroups\":[{\"Id\":\" \",\"Name\":\"Group\"}]")]
    [TestCase("\"ComboGroups\":[{\"Id\":\"g\",\"Name\":null}]")]
    [TestCase("\"ComboGroups\":[{\"Id\":\"g\",\"Name\":\" \"}]")]
    public void LoadSessionAsync_RejectsInvalidNestedEntriesAndEditorIdentities(string invalidField)
    {
        SessionService service = new(_jsRuntimeMock.Object, new RealJsonSerializer());
        InvalidOperationException? exception = Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.LoadSessionAsync($"{{\"SchemaVersion\":3,{invalidField}}}")
        );
        Assert.That(exception!.Message, Is.EqualTo("Invalid session file format"));
    }

    [Test]
    public async Task LoadSessionAsync_HistoricalV1_2UnversionedFixture_MigratesAndPreservesLegacySession()
    {
        // Unchanged bundled Fiendsmith/Bystial example from the v1.2.0 release.
        string fileContent = await ReadSessionFixture("legacy_v1_2_example_session.json");
        using JsonDocument document = JsonDocument.Parse(fileContent);
        JsonElement root = document.RootElement;
        Assert.That(root.TryGetProperty(nameof(SessionState.SchemaVersion), out _), Is.False);
        Assert.That(root.TryGetProperty("ComboGroups", out _), Is.False);
        Assert.That(root.TryGetProperty("CategoryColorIndices", out _), Is.False);

        foreach (JsonElement card in root.GetProperty("Cards").EnumerateArray())
        {
            Assert.That(card.TryGetProperty("Id", out _), Is.False);
            Assert.That(card.TryGetProperty("Active", out _), Is.False);
        }

        foreach (JsonElement combo in root.GetProperty("Combos").EnumerateArray())
        {
            Assert.That(combo.TryGetProperty("Cards", out _), Is.False);
            Assert.That(combo.TryGetProperty("Active", out _), Is.False);
            Assert.That(combo.TryGetProperty("GroupId", out _), Is.False);
        }

        SessionState session = await CreateRealSerializerSessionService()
            .LoadSessionAsync(fileContent);

        Assert.That(session.SchemaVersion, Is.EqualTo(SessionState.CurrentSchemaVersion));
        Assert.That(
            session.Categories.Select(category => category.Name),
            Is.EqualTo(
                new[]
                {
                    "1 Card Starter",
                    "Lubellion",
                    "Normal Summon",
                    "Bystial",
                    "L/D Normal Summon",
                }
            )
        );
        Assert.That(
            session.Cards.Select(card =>
                (
                    card.Name,
                    card.Copies,
                    Categories: string.Join("|", card.Categories.Select(category => category.Name))
                )
            ),
            Is.EqualTo(
                new[]
                {
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
                    ("Branded Regained", 1, ""),
                }
            )
        );
        Assert.That(
            session.Combos.Select(combo => combo.Name),
            Is.EqualTo(new[] { "1-Card Combo", "Moon Combo", "Moon Combo 2" })
        );
        Assert.That(
            session.Combos.Select(combo => combo.Categories.Count),
            Is.EqualTo(new[] { 1, 2, 2 })
        );
        Assert.That(
            session.Combos.SelectMany(combo =>
                combo.Categories.Select(category =>
                    (combo.Name, category.BaseCategory.Name, category.MinCount, category.MaxCount)
                )
            ),
            Is.EqualTo(
                new[]
                {
                    ("1-Card Combo", "1 Card Starter", 1, 5),
                    ("Moon Combo", "Lubellion", 1, 5),
                    ("Moon Combo", "Normal Summon", 1, 5),
                    ("Moon Combo 2", "Bystial", 1, 5),
                    ("Moon Combo 2", "L/D Normal Summon", 1, 5),
                }
            )
        );
        Assert.That(session.HandSize, Is.EqualTo(5));

        Assert.That(session.Cards.All(card => card.Active), Is.True);
        Assert.That(session.Combos.All(combo => combo.Active), Is.True);
        Assert.That(
            session.Combos.All(combo => combo.Cards.Count == 0 && combo.GroupId == null),
            Is.True
        );
        Assert.That(session.ComboGroups, Is.Empty);
        Assert.That(session.CategoryColorIndices, Is.Empty);
        Assert.That(
            session.Cards.All(card =>
                Guid.TryParseExact(card.Id, "N", out Guid id) && id != Guid.Empty
            ),
            Is.True
        );
        Assert.That(
            session.Cards.Select(card => card.Id).Distinct(StringComparer.Ordinal).Count(),
            Is.EqualTo(session.Cards.Count)
        );
    }

    [Test]
    public async Task LoadSessionAsync_CurrentEraUnversionedFixture_PreservesModernSessionData()
    {
        string fileContent = await ReadSessionFixture("vsmodel.json");
        JsonObject root = JsonNode.Parse(fileContent)!.AsObject();
        Assert.That(root.ContainsKey(nameof(SessionState.SchemaVersion)), Is.False);

        // Exercise inactive state as well as the active values in the real persisted fixture.
        root["Cards"]![1]!["Active"] = false;
        root["Combos"]![0]!["Active"] = false;

        SessionService service = CreateRealSerializerSessionService();
        SessionState session = await service.LoadSessionAsync(root.ToJsonString());

        Assert.That(session.SchemaVersion, Is.EqualTo(SessionState.CurrentSchemaVersion));
        Assert.That(
            session.Categories.Select(category => category.Name),
            Is.EqualTo(new[] { "Fire", "Dark", "Earth", "Level 5" })
        );
        Assert.That(session.Cards, Has.Count.EqualTo(25));
        Assert.That(session.Cards[0].Id, Is.EqualTo("6791c7d39e9d41cda86fa8f59b6092a1"));
        Assert.That(session.Cards[0].Name, Is.EqualTo("Maxx \"C\""));
        Assert.That(session.Cards[0].Copies, Is.EqualTo(1));
        Assert.That(session.Cards[1].Active, Is.False);
        Assert.That(
            session.Cards[5].Categories.Select(category => category.Name),
            Is.EqualTo(new[] { "Fire", "Level 5" })
        );
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
        Assert.That(
            session.ComboGroups.Select(group => group.Name),
            Is.EqualTo(new[] { "Full VS", "Full K9" })
        );
        Assert.That(session.HandSize, Is.EqualTo(5));
        Assert.That(
            session.CategoryColorIndices,
            Is.EqualTo(
                new Dictionary<string, int>
                {
                    ["Fire"] = 0,
                    ["Dark"] = 1,
                    ["Earth"] = 2,
                    ["Level 5"] = 3,
                }
            )
        );
    }

    [TestCase("razen_session.json")]
    [TestCase("vsmodel.json")]
    public async Task LoadSessionAsync_CurrentEraUnversionedRepositoryFixturesStillLoad(
        string fixtureName
    )
    {
        string fileContent = await File.ReadAllTextAsync(
            Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", fixtureName)
        );
        using JsonDocument document = JsonDocument.Parse(fileContent);
        Assert.That(
            document.RootElement.TryGetProperty(nameof(SessionState.SchemaVersion), out _),
            Is.False
        );

        SessionState session = await CreateRealSerializerSessionService()
            .LoadSessionAsync(fileContent);

        Assert.That(session.SchemaVersion, Is.EqualTo(SessionState.CurrentSchemaVersion));
        Assert.That(session.Cards, Has.Count.EqualTo(25));
    }

    [TestCase("legacy_v1_2_example_session.json")]
    [TestCase("vsmodel.json")]
    public async Task SaveSessionAsync_WritesCurrentVersionAndRoundTripsThroughRealSerializer(
        string fixtureName
    )
    {
        string fileContent = await ReadSessionFixture(fixtureName);
        SessionService loadService = CreateRealSerializerSessionService();
        SessionState loadedSession = await loadService.LoadSessionAsync(fileContent);
        SessionState sessionWithLegacyVersion = CopySession(loadedSession, schemaVersion: 0);
        CapturingJsRuntime jsRuntime = new();
        SessionService saveService = new(jsRuntime, new RealJsonSerializer());

        await saveService.SaveSessionAsync(sessionWithLegacyVersion, "round-trip");

        string savedJson = jsRuntime.DownloadedJson;
        using JsonDocument savedDocument = JsonDocument.Parse(savedJson);
        Assert.That(
            savedDocument.RootElement.GetProperty(nameof(SessionState.SchemaVersion)).GetInt32(),
            Is.EqualTo(SessionState.CurrentSchemaVersion)
        );
        Assert.That(
            sessionWithLegacyVersion.SchemaVersion,
            Is.EqualTo(0),
            "Saving should not mutate the caller's session state."
        );

        SessionState reloaded = await loadService.LoadSessionAsync(savedJson);
        Assert.That(reloaded.SchemaVersion, Is.EqualTo(SessionState.CurrentSchemaVersion));
        Assert.That(
            reloaded.Categories.Select(category => category.Name),
            Is.EqualTo(loadedSession.Categories.Select(category => category.Name))
        );
        Assert.That(
            reloaded.Cards.Select(card => (card.Id, card.Name, card.Copies, card.Active)),
            Is.EqualTo(
                loadedSession.Cards.Select(card => (card.Id, card.Name, card.Copies, card.Active))
            )
        );
        Assert.That(
            reloaded.Combos.Select(combo => (combo.Name, combo.GroupId, combo.Active)),
            Is.EqualTo(
                loadedSession.Combos.Select(combo => (combo.Name, combo.GroupId, combo.Active))
            )
        );
        Assert.That(
            reloaded.ComboGroups.Select(group => (group.Id, group.Name)),
            Is.EqualTo(loadedSession.ComboGroups.Select(group => (group.Id, group.Name)))
        );
        Assert.That(reloaded.HandSize, Is.EqualTo(loadedSession.HandSize));
        Assert.That(reloaded.CategoryColorIndices, Is.EqualTo(loadedSession.CategoryColorIndices));
    }

    [Test]
    public async Task LoadSessionAsync_VersionOnePayload_MigratesAndPreservesData()
    {
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

        SessionService service = CreateRealSerializerSessionService();
        SessionState session = await service.LoadSessionAsync(currentSessionJson);

        Assert.That(session.SchemaVersion, Is.EqualTo(SessionState.CurrentSchemaVersion));
        Assert.That(session.Cards[0].Active, Is.False);
        Assert.That(session.Cards[0].Copies, Is.EqualTo(2));
        Assert.That(session.Combos[0].Cards[0].CardId, Is.EqualTo("razen-id"));
        Assert.That(session.Combos[0].Active, Is.False);
        Assert.That(session.Combos[0].GroupId, Is.EqualTo("group-id"));
        Assert.That(session.ComboGroups[0].Name, Is.EqualTo("Main line"));
        Assert.That(session.CategoryColorIndices["Fire"], Is.EqualTo(3));
        Assert.That(
            session
                .Categories.Concat(session.Cards.SelectMany(c => c.Categories))
                .Concat(session.Combos.SelectMany(c => c.Categories).Select(c => c.BaseCategory))
                .All(c => c.Source == CategorySource.User && c.MetadataKey is null),
            Is.True
        );
    }

    [Test]
    public async Task MetadataSessionRoundTripsWithoutExternalLookupOrTopLevelPropertyDefinitions()
    {
        CategoryBase user = new("Spell");
        CategoryBase property = new("Spell", CategorySource.Metadata, "kind:spell");
        SessionState session = new()
        {
            Categories = [user],
            Cards = [new([user, property], 2, "Quick spell", externalCardId: 123)],
            Combos = [new([new(user, 1, 2), new(property, 1, 2)], "Two copies")],
            HandSize = 2,
            CategoryColorIndices = new() { ["Spell"] = 7 },
        };
        CapturingJsRuntime runtime = new();
        SessionService service = new(runtime, new RealJsonSerializer());
        await service.SaveSessionAsync(session, "metadata");
        SessionState loaded = await service.LoadSessionAsync(runtime.DownloadedJson);
        Assert.That(loaded.SchemaVersion, Is.EqualTo(SessionState.CurrentSchemaVersion));
        Assert.That(loaded.Categories, Is.EqualTo(new[] { user }));
        Assert.That(loaded.Cards[0].Categories, Is.EqualTo(new[] { user, property }));
        Assert.That(loaded.Cards[0].ExternalCardId, Is.EqualTo(123));
        Assert.That(loaded.Cards[0].Id, Is.EqualTo(session.Cards[0].Id));
        Assert.That(
            loaded.Combos[0].Categories.Select(c => c.BaseCategory),
            Is.EqualTo(new[] { user, property })
        );
        Assert.That(loaded.CategoryColorIndices, Is.EqualTo(session.CategoryColorIndices));
        Assert.That(
            new ProbabilityCalculatorService().CalculateProbabilityForCombos(
                loaded.Cards,
                loaded.Combos,
                2
            ),
            Is.EqualTo(SmallDeckOracle.EnumerateProbability(session.Cards, session.Combos, 2))
        );
    }

    [TestCase(0)]
    [TestCase(1)]
    public void MigrationExplicitlyClassifiesAllPreV2CategoryLocations(int version)
    {
        string json = $$$"""
            { "SchemaVersion": {{{version}}}, "Categories": [{"Name":"Top", "Source":"Metadata", "MetadataKey":"spoof"}],
              "Cards": [{"Categories":[{"Name":"On card"}]}],
              "Combos": [{"Categories":[{"BaseCategory":{"Name":"On combo"}}]}] }
            """;
        string migrated = new SessionSchemaMigrator().MigrateToCurrent(json);
        using JsonDocument document = JsonDocument.Parse(migrated);
        JsonElement root = document.RootElement;
        Assert.That(
            root.GetProperty("SchemaVersion").GetInt32(),
            Is.EqualTo(SessionState.CurrentSchemaVersion)
        );
        JsonElement[] locations =
        [
            root.GetProperty("Categories")[0],
            root.GetProperty("Cards")[0].GetProperty("Categories")[0],
            root.GetProperty("Combos")[0].GetProperty("Categories")[0].GetProperty("BaseCategory"),
        ];

        foreach (JsonElement category in locations)
        {
            Assert.That(category.GetProperty("Source").GetString(), Is.EqualTo("User"));
            Assert.That(category.TryGetProperty("MetadataKey", out _), Is.False);
        }

        Assert.That(new SessionSchemaMigrator().MigrateToCurrent(migrated), Is.EqualTo(migrated));
    }

    [Test]
    public void LoadSessionAsync_FutureSchemaVersion_IsRejectedClearly()
    {
        InvalidOperationException? exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () =>
                await CreateRealSerializerSessionService()
                    .LoadSessionAsync("{ \"SchemaVersion\": 4 }")
        );

        Assert.That(exception!.Message, Does.Contain("Unsupported session schema version 4"));
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
    public void LoadSessionAsync_MalformedSchemaOrRoot_IsRejectedAsInvalidSession(
        string fileContent
    )
    {
        InvalidOperationException? exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () =>
                await CreateRealSerializerSessionService().LoadSessionAsync(fileContent)
        );

        Assert.That(exception!.Message, Is.EqualTo("Invalid session file format"));
    }

    [Test]
    public async Task LoadSessionAsync_CurrentEraUnversionedFixtureStillRejectsDuplicateCardIds()
    {
        JsonObject root = JsonNode.Parse(await ReadSessionFixture("vsmodel.json"))!.AsObject();
        JsonArray cards = root["Cards"]!.AsArray();
        cards.Add(cards[0]!.DeepClone());

        InvalidOperationException? exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () =>
                await CreateRealSerializerSessionService().LoadSessionAsync(root.ToJsonString())
        );

        Assert.That(exception!.Message, Does.Contain("duplicate card IDs"));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public async Task MissingMaximumModeLoadsFixedForEveryHistoricalSchema(int version)
    {
        string versionField = version == 0 ? "" : $"\"SchemaVersion\":{version},";
        string json = $$"""
            { {{versionField}} "Categories":[{"Name":"Starter"}],
              "Cards":[{"Id":"starter","Name":"Starter","Copies":6,"Categories":[{"Name":"Starter"}]}],
              "Combos":[{"Categories":[{"BaseCategory":{"Name":"Starter"},"MinCount":1,"MaxCount":5},
                                        {"BaseCategory":{"Name":"Starter"},"MinCount":0,"MaxCount":0}],
                         "Cards":[{"CardId":"starter","MinCount":1,"MaxCount":5},
                                   {"CardId":"starter","MinCount":0,"MaxCount":0}]}],"HandSize":5 }
            """;
        string migrated = new SessionSchemaMigrator().MigrateToCurrent(json);
        using JsonDocument document = JsonDocument.Parse(migrated);
        JsonElement requirements = document.RootElement.GetProperty("Combos")[0];

        if (version < 2)
        {
            Assert.That(
                requirements.GetProperty("Categories")[0].GetProperty("MaximumMode").GetString(),
                Is.EqualTo("Fixed")
            );
            Assert.That(
                requirements.GetProperty("Cards")[0].GetProperty("MaximumMode").GetString(),
                Is.EqualTo("Fixed")
            );
        }
        else if (version == SessionState.CurrentSchemaVersion)
        {
            Assert.That(migrated, Is.EqualTo(json), "Current-version sessions bypass migration.");
        }

        Assert.That(new SessionSchemaMigrator().MigrateToCurrent(migrated), Is.EqualTo(migrated));
        SessionState loaded = await CreateRealSerializerSessionService().LoadSessionAsync(json);
        ComboCategory category = loaded.Combos[0].Categories[0];
        ComboCard card = loaded.Combos[0].Cards[0];
        Assert.That(category.MaximumMode, Is.EqualTo(RequirementMaximumMode.Fixed));
        Assert.That(category.MaxCount, Is.EqualTo(5));
        Assert.That(category.GetEffectiveMaximum(6), Is.EqualTo(5));
        Assert.That(card.MaximumMode, Is.EqualTo(RequirementMaximumMode.Fixed));
        Assert.That(card.GetEffectiveMaximum(6), Is.EqualTo(5));
        Assert.That(
            loaded.Combos[0].Categories[1].MaximumMode,
            Is.EqualTo(RequirementMaximumMode.Fixed)
        );
        Assert.That(
            loaded.Combos[0].Cards[1].MaximumMode,
            Is.EqualTo(RequirementMaximumMode.Fixed)
        );
        Assert.That(loaded.Combos[0].Categories[1].GetEffectiveMaximum(6), Is.Zero);
        Assert.That(loaded.Combos[0].Cards[1].GetEffectiveMaximum(6), Is.Zero);
    }

    [Test]
    public async Task DynamicAndFixedV2RequirementsRoundTripIncludingImpossibleMinimumAndZero()
    {
        CategoryBase category = new("Starter");
        Card card = new([category], 6, "Starter");
        SessionState session = new()
        {
            Categories = [category],
            Cards = [card],
            HandSize = 5,
            Combos =
            [
                new(
                    [new(category, 1, 5, RequirementMaximumMode.HandSize), new(category, 0, 0)],
                    cards:
                    [
                        new(card.Id, 1, 5, RequirementMaximumMode.HandSize),
                        new(card.Id, 1, 5),
                        new(card.Id, 0, 0),
                    ]
                ),
                new(
                    [new(category, 6, 5, RequirementMaximumMode.HandSize)],
                    cards: [new(card.Id, 6, 5, RequirementMaximumMode.HandSize)]
                ),
            ],
        };
        CapturingJsRuntime runtime = new();
        SessionService service = new(runtime, new RealJsonSerializer());
        await service.SaveSessionAsync(session, "modes");
        Assert.That(
            runtime.DownloadedJson,
            Does.Contain("\"MaximumMode\": \"HandSize\"").And.Contain("\"MaximumMode\": \"Fixed\"")
        );
        SessionState loaded = await service.LoadSessionAsync(runtime.DownloadedJson);
        Assert.That(loaded.SchemaVersion, Is.EqualTo(SessionState.CurrentSchemaVersion));
        Assert.That(
            loaded.Combos[0].Categories.Select(c => (c.MinCount, c.MaxCount, c.MaximumMode)),
            Is.EqualTo(
                session.Combos[0].Categories.Select(c => (c.MinCount, c.MaxCount, c.MaximumMode))
            )
        );
        Assert.That(
            loaded.Combos[0].Cards.Select(c => (c.MinCount, c.MaxCount, c.MaximumMode)),
            Is.EqualTo(session.Combos[0].Cards.Select(c => (c.MinCount, c.MaxCount, c.MaximumMode)))
        );
        Assert.That(
            loaded.Combos[0].Categories.Select(c => c.GetEffectiveMaximum(6)),
            Is.EqualTo(new[] { 6, 0 })
        );
        Assert.That(
            loaded.Combos[0].Cards.Select(c => c.GetEffectiveMaximum(6)),
            Is.EqualTo(new[] { 6, 5, 0 })
        );
        Assert.That(
            new ProbabilityCalculatorService().CalculateProbabilityForCombos(
                loaded.Cards,
                [loaded.Combos[1]],
                5
            ),
            Is.Zero
        );
        Assert.That(
            loaded.Combos[1].Categories[0].MaximumMode,
            Is.EqualTo(RequirementMaximumMode.HandSize)
        );
        Assert.That(
            loaded.Combos[1].Cards[0].MaximumMode,
            Is.EqualTo(RequirementMaximumMode.HandSize)
        );
    }

    [TestCase("\"Unexpected\"")]
    [TestCase("\"0\"")]
    [TestCase("0")]
    [TestCase("null")]
    [TestCase("true")]
    [TestCase("\"HandSize\",\"maximumMode\":\"Fixed\"")]
    public void MalformedMaximumModeIsRejectedForBothRequirementKinds(string mode)
    {
        foreach (string kind in new[] { "Categories", "Cards" })
        {
            string selector =
                kind == "Categories" ? "\"BaseCategory\":{\"Name\":\"A\"}" : "\"CardId\":\"a\"";
            string json = $$"""
                {"SchemaVersion":2,"Combos":[{"Categories":[],"{{kind}}":[{ {{selector}},"MinCount":0,"MaxCount":0,"MaximumMode":{{mode}} }]}]}
                """;

            // Avoid a duplicate Categories property when testing category constraints.
            if (kind == "Categories")
            {
                json = json.Replace("\"Categories\":[],", "");
            }

            InvalidOperationException? exception = Assert.ThrowsAsync<InvalidOperationException>(
                async () =>
                    await CreateRealSerializerSessionService().LoadSessionAsync(json)
            );
            Assert.That(exception!.Message, Is.EqualTo("Invalid session file format"));
            Assert.That(exception.InnerException, Is.TypeOf<JsonException>());
            Assert.That(exception.InnerException!.Message, Does.Contain("maximum mode"));
        }
    }

    private static async Task<string> ReadSessionFixture(string fixtureName) =>
        await File.ReadAllTextAsync(
            Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", fixtureName)
        );

    private SessionService CreateRealSerializerSessionService() =>
        new(_jsRuntimeMock.Object, new RealJsonSerializer());

    private static SessionState CopySession(SessionState session, int schemaVersion) =>
        new()
        {
            SchemaVersion = schemaVersion,
            Categories = session.Categories,
            Cards = session.Cards,
            Combos = session.Combos,
            ComboGroups = session.ComboGroups,
            HandSize = session.HandSize,
            CategoryColorIndices = session.CategoryColorIndices,
        };

    private sealed class CapturingJsRuntime : IJSRuntime
    {
        private object?[]? _lastArguments;
        public string? InvokedFunction { get; private set; }

        public string DownloadedJson =>
            Encoding.UTF8.GetString(Convert.FromBase64String((string)_lastArguments![1]!));

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            InvokedFunction = identifier;
            _lastArguments = args;

            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args
        ) => InvokeAsync<TValue>(identifier, args);
    }

    private sealed class FailingJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            ValueTask.FromException<TValue>(new JSException("Session file write failed."));

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args
        ) => InvokeAsync<TValue>(identifier, args);
    }
}
