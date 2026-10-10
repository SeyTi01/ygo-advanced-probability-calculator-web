using Microsoft.JSInterop;
using Moq;
using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.Session;
using YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;
using Serializer = YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer;

namespace YGOProbabilityCalculatorBlazorTest.Services.Session;

[TestFixture]
public class LegacyCardMetadataEnricherTest {
    private static readonly CardInfo Ash = new() { Id = 14558127, Name = "Ash Blossom & Joyous Spring",
        Type = "Tuner Monster", Race = "Zombie", Attribute = "FIRE", Level = 3 };
    private static readonly CardInfo Maxx = new() { Id = 23434538, Name = "Maxx \"C\"",
        Type = "Effect Monster", Race = "Insect", Attribute = "EARTH", Level = 2 };

    [TestCase(0)]
    [TestCase(1)]
    public async Task MigratedSessionPreservesStateProbabilitiesAndSavesSelfContainedMetadata(int version) {
        string json = """
            {"Categories":[{"Name":"Monster"},{"Name":"Starter"}],
             "Cards":[{"Id":"ash","Name":"Ash Blossom & Joyous Spring","Copies":2,"Active":true,"Categories":[{"Name":"Monster"},{"Name":"Starter"}]},
                      {"Id":"maxx","Name":"Maxx \"C\"","Copies":2,"Active":true,"Categories":[{"Name":"Starter"}]},
                      {"Id":"inactive","Name":"Ash Blossom & Joyous Spring","Copies":3,"Active":false,"Categories":[{"Name":"Monster"}]},
                      {"Id":"custom","Name":"My Custom Card","Copies":1,"Active":true,"Categories":[]}],
             "Combos":[{"Name":"Existing route","GroupId":"group","Active":true,
                 "Categories":[{"BaseCategory":{"Name":"Starter"},"MinCount":1,"MaxCount":2}],
                 "Cards":[{"CardId":"ash","MinCount":1,"MaxCount":2}]}],
             "ComboGroups":[{"Id":"group","Name":"Existing group"}],"HandSize":2,"CategoryColorIndices":{"Monster":3,"Starter":1}}
            """;
        if (version == 1) {
            json = json.Insert(1, "\"SchemaVersion\":1,");
        }
        CaptureJs js = new();
        SessionService sessions = new(js, new Serializer());
        SessionState session = await sessions.LoadSessionAsync(json);
        Card[] originalCards = session.Cards.ToArray();
        Combo[] originalCombos = session.Combos.ToArray();
        ComboGroup[] originalGroups = session.ComboGroups.ToArray();
        CategoryBase[] originalCategories = session.Categories.ToArray();
        ProbabilityCalculatorService engine = new();
        List<Card> active = session.Cards.Where(card => card.Active).ToList();
        double oracleBefore = SmallDeckOracle.EnumerateProbability(active, session.Combos, session.HandSize);
        ProbabilityCalculationResult before = engine.CalculateProbabilityResults(active, session.Combos, session.HandSize, session.ComboGroups);
        Mock<ICardInfoService> metadata = MatchingService(Ash, Maxx);

        await new LegacyCardMetadataEnricher(metadata.Object).EnrichAsync(session);

        metadata.Verify(service => service.GetCardInfoByExactNamesAsync(It.Is<IEnumerable<string>>(names =>
            names.SequenceEqual(new[] { Ash.Name, Maxx.Name, "My Custom Card" }))), Times.Once);
        Assert.That(session.Cards.Select(card => (card.Id, card.Name, card.Copies, card.Active)),
            Is.EqualTo(originalCards.Select(card => (card.Id, card.Name, card.Copies, card.Active))));
        for (int i = 0; i < originalCards.Length; i++) {
            Assert.That(session.Cards[i].Categories.Where(category => category.Source == CategorySource.User),
                Is.EqualTo(originalCards[i].Categories));
        }
        Assert.That(session.Categories, Is.EqualTo(originalCategories));
        Assert.That(session.Combos, Is.EqualTo(originalCombos));
        Assert.That(session.ComboGroups, Is.EqualTo(originalGroups));
        Assert.That(session.CategoryColorIndices, Is.EquivalentTo(new Dictionary<string, int> { ["Monster"] = 3, ["Starter"] = 1 }));
        Assert.That(session.Cards.Select(card => card.ExternalCardId), Is.EqualTo(new int?[] { Ash.Id, Maxx.Id, Ash.Id, null }));
        Assert.That(session.Cards[0].Categories.Where(category => category.Name == "Monster").Select(category => category.Source),
            Is.EqualTo(new[] { CategorySource.User, CategorySource.Metadata }));
        Assert.That(session.Cards[0].Categories.Select(category => category.Identity).Distinct().Count(),
            Is.EqualTo(session.Cards[0].Categories.Count));
        Assert.That(session.Cards[3], Is.SameAs(originalCards[3]));
        List<Card> enrichedActive = session.Cards.Where(card => card.Active).ToList();
        double oracleAfter = SmallDeckOracle.EnumerateProbability(enrichedActive, session.Combos, session.HandSize);
        ProbabilityCalculationResult after = engine.CalculateProbabilityResults(enrichedActive, session.Combos, session.HandSize, session.ComboGroups);
        Assert.That(oracleAfter, Is.EqualTo(oracleBefore));
        Assert.That(before.TotalProbability, Is.EqualTo(oracleBefore).Within(1e-12));
        Assert.That(after.TotalProbability, Is.EqualTo(oracleAfter).Within(1e-12));
        Assert.That(after.ComboProbabilities.Select(result => result.Probability), Is.EqualTo(before.ComboProbabilities.Select(result => result.Probability)));
        Assert.That(after.GroupProbabilities!.Select(result => result.Probability), Is.EqualTo(before.GroupProbabilities!.Select(result => result.Probability)));

        await sessions.SaveSessionAsync(session, "enriched.json");
        using JsonDocument saved = JsonDocument.Parse(js.Json);
        Assert.That(saved.RootElement.GetProperty("SchemaVersion").GetInt32(), Is.EqualTo(SessionState.CurrentSchemaVersion));
        SessionState reloaded = await sessions.LoadSessionAsync(js.Json);
        Mock<ICardInfoService> offline = new(MockBehavior.Strict);
        // The custom card still gets a best-effort attempt; even a thrown lookup cannot lose saved memberships.
        await new LegacyCardMetadataEnricher(offline.Object).EnrichAsync(reloaded);
        offline.Verify(service => service.GetCardInfoByExactNamesAsync(It.Is<IEnumerable<string>>(names =>
            names.SequenceEqual(new[] { "My Custom Card" }))), Times.Once);
        Assert.That(reloaded.Cards[0].ExternalCardId, Is.EqualTo(Ash.Id));
        Assert.That(reloaded.Cards[0].Categories.Select(category => category.Identity),
            Is.EqualTo(session.Cards[0].Categories.Select(category => category.Identity)));
    }

    [Test]
    public async Task AuthenticV0FixtureCanBeEnrichedWithoutChangingLegacyRolesOrConstraints() {
        SessionService sessions = new(new CaptureJs(), new Serializer());
        string source = await File.ReadAllTextAsync(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "legacy_v1_2_example_session.json"));
        SessionState session = await sessions.LoadSessionAsync(source);
        string original = JsonSerializer.Serialize(session);
        Card[] originalCards = session.Cards.ToArray();
        await new LegacyCardMetadataEnricher(MatchingService(Ash, Maxx).Object).EnrichAsync(session);
        Assert.That(session.Cards.Single(card => card.Name == Ash.Name).ExternalCardId, Is.EqualTo(Ash.Id));
        Assert.That(session.Cards.Single(card => card.Name == Maxx.Name).ExternalCardId, Is.EqualTo(Maxx.Id));
        List<Card> restoredCards = session.Cards.Select(card => new Card(card.Categories.Where(category => category.Source == CategorySource.User),
            card.Copies, card.Name, card.Active, card.Id)).ToList();
        Assert.That(JsonSerializer.Serialize(new SessionState { Cards = restoredCards, Categories = session.Categories,
            Combos = session.Combos, ComboGroups = session.ComboGroups, HandSize = session.HandSize,
            CategoryColorIndices = session.CategoryColorIndices }), Is.EqualTo(original));
        Assert.That(session.Cards.Select(card => card.Id), Is.EqualTo(originalCards.Select(card => card.Id)));
    }

    [Test]
    public async Task FailedOrUnrelatedResolutionLeavesCardsUntouched() {
        Card card = new([], 3, Ash.Name, active: false);
        SessionState session = new() { Cards = [card] };
        Mock<ICardInfoService> wrong = MatchingService(Maxx);
        wrong.Setup(service => service.GetCardInfoByExactNamesAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(new Dictionary<string, CardInfo> { [Ash.Name] = Maxx });
        await new LegacyCardMetadataEnricher(wrong.Object).EnrichAsync(session);
        Assert.That(session.Cards[0], Is.SameAs(card));
        wrong.Setup(service => service.GetCardInfoByExactNamesAsync(It.IsAny<IEnumerable<string>>()))
            .ThrowsAsync(new HttpRequestException("Offline"));
        await new LegacyCardMetadataEnricher(wrong.Object).EnrichAsync(session);
        Assert.That(session.Cards[0], Is.SameAs(card));
    }

    [Test]
    public async Task CompleteBundledExampleIsCurrentSchemaAndRequiresNoMetadataServiceOrApi() {
        string source = await File.ReadAllTextAsync(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "example_session_state.json"));
        using JsonDocument document = JsonDocument.Parse(source);
        Assert.That(document.RootElement.GetProperty("SchemaVersion").GetInt32(), Is.EqualTo(SessionState.CurrentSchemaVersion));
        SessionState session = await new SessionService(new CaptureJs(), new Serializer()).LoadSessionAsync(source);
        Assert.That(session.Cards, Has.Count.EqualTo(25));
        Assert.That(session.Categories.Select(category => category.Name), Is.EqualTo(new[] { "VS Monster", "VS Starter", "K9 Starter" }));
        Assert.That(session.Cards.All(card => card.ExternalCardId is > 0 && card.Categories.Any(category => category.Source == CategorySource.Metadata)), Is.True);
        Assert.That(session.Combos, Has.Count.EqualTo(8));
        Assert.That(session.HandSize, Is.EqualTo(5));
        Card ash = session.Cards.Single(card => card.Name == Ash.Name);
        Assert.That(ash.ExternalCardId, Is.EqualTo(Ash.Id));
        Assert.That(ash.Categories.Select(category => category.Identity), Does.Contain("metadata:monster-trait:tuner").And.Contain("metadata:attribute:fire").And.Not.Contain("user:Fire"));
        Card spell = session.Cards.Single(card => card.Name == "K9-X Forced Release");
        Assert.That(spell.Categories.Select(category => category.Identity), Does.Contain("metadata:kind:spell").And.Contain("metadata:spell-type:quick-play"));
        Mock<ICardInfoService> offline = new(MockBehavior.Strict);
        await new LegacyCardMetadataEnricher(offline.Object).EnrichAsync(session);
        offline.VerifyNoOtherCalls();
    }

    private static Mock<ICardInfoService> MatchingService(params CardInfo[] cards) {
        Mock<ICardInfoService> service = new();
        service.Setup(service => service.GetCardInfoByExactNamesAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(cards.ToDictionary(card => card.Name, StringComparer.Ordinal));
        return service;
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ManualOnlyPropertiesAreEnrichedAndObjectiveOverlapBecomesReadOnly(bool overlapping) {
        CategoryBase property = overlapping
            ? new CategoryBase("Attribute: FIRE", CategorySource.Metadata, "attribute:fire")
            : new CategoryBase("Attribute: DARK", CategorySource.Metadata, "attribute:dark");
        CategoryBase user = new("Role");
        Card original = new Card([user], 3, Ash.Name, false, "legacy").WithManualMetadataCategory(property);
        SessionState session = new() { Cards = [original] };
        Mock<ICardInfoService> service = MatchingService(Ash);
        await new LegacyCardMetadataEnricher(service.Object).EnrichAsync(session);
        service.Verify(s => s.GetCardInfoByExactNamesAsync(It.Is<IEnumerable<string>>(names => names.SequenceEqual(new[] { Ash.Name }))), Times.Once);
        Card enriched = session.Cards.Single();
        Assert.That((enriched.Id, enriched.Name, enriched.Copies, enriched.Active),
            Is.EqualTo((original.Id, original.Name, original.Copies, original.Active)));
        Assert.That(enriched.ExternalCardId, Is.EqualTo(Ash.Id));
        Assert.That(enriched.Categories, Does.Contain(user).And.Contain(property));
        Assert.That(enriched.Categories.Select(c => c.Identity).Distinct().Count(), Is.EqualTo(enriched.Categories.Count));
        Assert.That(enriched.ManualMetadataCategoryKeys.Contains(property.MetadataKey!), Is.EqualTo(!overlapping));
        Card removed = enriched.WithoutManualMetadataCategory(property.MetadataKey!);
        Assert.That(removed.Categories.Contains(property), Is.EqualTo(overlapping));
        Assert.That(removed.Categories.Select(c => c.Identity), Does.Contain("metadata:attribute:fire"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task V2SaveLoadRetainsManualMembershipOfflineIncludingFailedEnrichment(bool externalId) {
        CategoryBase fire = new("Attribute: FIRE", CategorySource.Metadata, "attribute:fire");
        Card original = new Card([], 2, "Functional access", false, "manual", externalId ? 123 : null)
            .WithManualMetadataCategory(fire);
        CaptureJs js = new();
        SessionService sessions = new(js, new Serializer());
        await sessions.SaveSessionAsync(new SessionState { Cards = [original] }, "manual.json");
        Assert.That(SessionState.CurrentSchemaVersion, Is.EqualTo(4));
        using JsonDocument saved = JsonDocument.Parse(js.Json);
        Assert.That(saved.RootElement.GetProperty("SchemaVersion").GetInt32(), Is.EqualTo(SessionState.CurrentSchemaVersion));
        SessionState loaded = await sessions.LoadSessionAsync(js.Json);
        Mock<ICardInfoService> offline = new(MockBehavior.Strict);
        offline.Setup(s => s.GetCardInfoByExactNamesAsync(It.IsAny<IEnumerable<string>>())).ThrowsAsync(new HttpRequestException("Offline"));
        await new LegacyCardMetadataEnricher(offline.Object).EnrichAsync(loaded);
        Card card = loaded.Cards.Single();
        Assert.That(card.ManualMetadataCategoryKeys, Is.EquivalentTo(original.ManualMetadataCategoryKeys));
        Assert.That(card.Categories, Is.EqualTo(original.Categories));
        Assert.That((card.Id, card.ExternalCardId, card.Copies, card.Name, card.Active),
            Is.EqualTo((original.Id, original.ExternalCardId, original.Copies, original.Name, original.Active)));
        offline.Verify(s => s.GetCardInfoByExactNamesAsync(It.IsAny<IEnumerable<string>>()), externalId ? Times.Never() : Times.Once());
    }

    [Test]
    public async Task PreviewV2WithoutProvenanceLoadsWithNoManualProperties() {
        string source = await File.ReadAllTextAsync(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "example_session_state.json"));
        source = System.Text.RegularExpressions.Regex.Replace(
            source,
            @",\s*""ManualMetadataCategoryKeys"":\s*\[[^\]]*\]",
            "",
            System.Text.RegularExpressions.RegexOptions.Singleline);
        using JsonDocument document = JsonDocument.Parse(source);
        Assert.That(document.RootElement.GetProperty("Cards").EnumerateArray().All(c => !c.TryGetProperty("ManualMetadataCategoryKeys", out _)), Is.True);
        SessionState session = await new SessionService(new CaptureJs(), new Serializer()).LoadSessionAsync(source);
        Assert.That(session.Cards.All(c => c.ManualMetadataCategoryKeys.Count == 0), Is.True);
        Assert.That(session.Cards.All(c => c.Categories.Any(p => p.Source == CategorySource.Metadata)), Is.True);
    }

    private sealed class CaptureJs : IJSRuntime {
        public string Json { get; private set; } = "";
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) {
            Json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String((string)args![1]!));
            return ValueTask.FromResult(default(TValue)!);
        }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<TValue>(identifier, args);
    }
}
