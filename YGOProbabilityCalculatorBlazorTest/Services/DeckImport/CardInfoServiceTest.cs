using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Services.DeckImport;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazorTest.Services.DeckImport;

[TestFixture]
public class CardInfoServiceTests {
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task LocalSearchRanksExactPrefixAndSubstringMatchesAndLoadsOnceWithoutApiRequests() {
        CardInfo[] cards = [
            new() { Id = 4, Name = "Legendary Ash Blossom & Joyous Spring", Type = "Effect Monster", ArtworkMetadataKnown = true },
            new() { Id = 3, Name = "Ash Blossom & Joyous Spring (Promo)", Type = "Effect Monster", ArtworkMetadataKnown = true },
            new() { Id = 9, Name = "Ash Blossom & Joyous Spring", Type = "Effect Monster", ArtworkMetadataKnown = true },
            new() { Id = 2, Name = "Ash Blossom & Joyous Spring", Type = "Effect Monster", ArtworkMetadataKnown = true },
            .. Enumerable.Range(10, 25).Select(index => new CardInfo {
                Id = index,
                Name = $"Fire Collector {index:00}",
                Type = "Effect Monster",
                ArtworkMetadataKnown = true
            })
        ];
        string catalogJson = CreateLocalCatalogJson(cards);
        RecordingHttpMessageHandler handler = new((request, _) => Task.FromResult(
            request.RequestUri!.AbsolutePath.EndsWith("card-catalog.v1.json", StringComparison.Ordinal)
                ? Response(HttpStatusCode.OK, catalogJson)
                : Response(HttpStatusCode.NotFound, "{}")));
        using HttpClient client = new(handler) { BaseAddress = new Uri("https://calculator.test/") };
        CardInfoService service = new(new TestLocalStorage(null), client, new TestTimeProvider(Now));

        Assert.That(await service.SearchCardsAsync("  "), Is.Empty);
        Assert.That(await service.SearchCardsAsync(" A "), Is.Empty);
        IReadOnlyList<CardInfo> ranked = await service.SearchCardsAsync("  aSh Blossom & Joyous Spring  ");
        Assert.That(ranked.Select(card => card.Id), Is.EqualTo(new[] { 2, 9, 3, 4 }));
        IReadOnlyList<CardInfo> capped = await service.SearchCardsAsync("Fire");
        Assert.That(capped, Has.Count.EqualTo(20));
        string[] sortedNames = capped.Select(card => card.Name)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Assert.That(capped.Select(card => card.Name).ToArray(), Is.EqualTo(sortedNames));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
        Assert.That(handler.Requests.Single(), Does.StartWith("https://calculator.test/data/card-catalog.v1.json"));
        Assert.That(handler.Requests.Any(request => request.Contains("ygoprodeck", StringComparison.OrdinalIgnoreCase)), Is.False);
    }

    [Test]
    public async Task LocalSearchReturnsCompleteCardInfoAndArtworkWithoutAnotherRequest() {
        CardInfo expected = new() {
            Id = 14558127,
            Name = "Ash Blossom & Joyous Spring",
            Type = "XYZ Pendulum Effect Monster",
            FrameType = "xyz_pendulum",
            Race = "Zombie",
            Attribute = "FIRE",
            Level = 4,
            LinkVal = 2,
            Scale = 8,
            Archetype = "Floowandereeze",
            CanonicalCardId = 14558127,
            ArtworkImageIds = [14558127, 999999],
            ArtworkMetadataKnown = true
        };
        RecordingHttpMessageHandler handler = new((_, _) => Task.FromResult(Response(HttpStatusCode.OK,
            CreateLocalCatalogJson([expected]))));
        using HttpClient client = new(handler) { BaseAddress = new Uri("https://calculator.test/") };
        CardInfoService service = new(new TestLocalStorage(null), client, new TestTimeProvider(Now));

        CardInfo selected = (await service.SearchCardsAsync("Ash Blossom")).Single();
        Assert.That((selected.Id, selected.Name, selected.Type, selected.FrameType, selected.Race, selected.Attribute,
            selected.Level, selected.LinkVal, selected.Scale, selected.Archetype, selected.CanonicalCardId,
            selected.ArtworkMetadataKnown),
            Is.EqualTo((expected.Id, expected.Name, expected.Type, expected.FrameType, expected.Race, expected.Attribute,
                expected.Level, expected.LinkVal, expected.Scale, expected.Archetype, expected.CanonicalCardId,
                expected.ArtworkMetadataKnown)));
        Assert.That(selected.ArtworkImageIds, Is.EqualTo(expected.ArtworkImageIds));
        Assert.That(CardPropertyProvider.GetCategories(selected).Select(category => category.Name),
            Does.Contain("Xyz Monster").And.Contain("Pendulum Monster").And.Contain("Rank 4")
                .And.Contain("Pendulum Scale 8").And.Contain("Attribute: FIRE").And.Contain("Monster Type: Zombie"));
        CardInfo loaded = await service.GetCardInfoAsync(expected.Id);
        Assert.That((loaded.Id, loaded.Name, loaded.Type, loaded.FrameType, loaded.Race, loaded.Attribute,
            loaded.Level, loaded.LinkVal, loaded.Scale, loaded.Archetype, loaded.CanonicalCardId,
            loaded.ArtworkMetadataKnown),
            Is.EqualTo((expected.Id, expected.Name, expected.Type, expected.FrameType, expected.Race, expected.Attribute,
                expected.Level, expected.LinkVal, expected.Scale, expected.Archetype, expected.CanonicalCardId,
                expected.ArtworkMetadataKnown)));
        Assert.That(loaded.ArtworkImageIds, Is.EqualTo(expected.ArtworkImageIds));
        Assert.That((await service.GetCardArtworkInfoAsync(expected.Id)).ArtworkImageIds, Is.EqualTo(expected.ArtworkImageIds));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
    }

    [Test]
    public async Task IdOutsideLocalCatalogUsesOnlyTargetedMetadataFallback() {
        string catalogJson = CreateLocalCatalogJson([
            new CardInfo { Id = 123, Name = "Local", Type = "Spell Card", ArtworkMetadataKnown = true }
        ]);
        RecordingHttpMessageHandler handler = new((request, _) => Task.FromResult(
            request.RequestUri!.AbsolutePath.EndsWith("card-catalog.v1.json", StringComparison.Ordinal)
                ? Response(HttpStatusCode.OK, catalogJson)
                : Response(HttpStatusCode.OK, "{\"data\":[{\"id\":987654,\"name\":\"Fallback\",\"type\":\"Effect Monster\"}]}")));
        using HttpClient client = new(handler) { BaseAddress = new Uri("https://calculator.test/") };
        CardInfoService service = new(new TestLocalStorage(null), client, new TestTimeProvider(Now));

        CardInfo info = await service.GetCardInfoAsync(987654);
        Assert.That(info.Name, Is.EqualTo("Fallback"));
        Assert.That(handler.Requests, Has.Length.EqualTo(2));
        Assert.That(handler.Requests[0], Does.Contain("card-catalog.v1.json"));
        Assert.That(handler.Requests[1], Does.EndWith("cardinfo.php?id=987654"));
        Assert.That(handler.Requests.Any(request => request.EndsWith("cardinfo.php", StringComparison.Ordinal)), Is.False);
    }

    private const string ArtworkCardJson = """
        {"id":1234,"name":"Art","type":"Effect Monster","card_images":[
          {"id":2222,"image_url_small":"https://images.ygoprodeck.com/images/cards_small/2222.jpg"},
          {"id":1234,"image_url_small":"https://images.ygoprodeck.com/images/cards_small/1234.jpg"},
          {"id":100000101,"image_url_small":"https://images.ygoprodeck.com/images/cards_small/100000101.jpg"},
          {"id":6666,"image_url_small":"https://evil.test/6666.jpg"},
          {"id":"bad"},null,42]}
        """;

    [Test]
    public async Task ArtworkApiCacheRoundTripPreservesCanonicalAndAlternateIdsWithoutProviderUrls() {
        TestLocalStorage storage = new(null);
        RecordingHttpMessageHandler handler = new((_, _) => Task.FromResult(Response(HttpStatusCode.OK,
            "{\"data\":[" + ArtworkCardJson + "]}")));
        using HttpClient client = new(handler);
        CardInfoService service = new(storage, client, new TestTimeProvider(Now));
        CardInfo info = await service.GetCardArtworkInfoAsync(1234);
        Assert.That(info.ArtworkImageIds, Is.EqualTo(new[] {2222, 1234, 100000101}));
        Assert.That(info.CanonicalCardId, Is.EqualTo(1234));
        Assert.That(info.SelectArtworkImageId(1234), Is.EqualTo(1234));
        Assert.That(info.SelectArtworkImageId(2222), Is.EqualTo(2222));
        Assert.That(info.SelectArtworkImageId(5555), Is.EqualTo(1234));
        Assert.That((info with { CanonicalCardId = 9999 }).SelectArtworkImageId(5555), Is.EqualTo(2222));
        Assert.That(storage.RawCache, Does.Not.Contain("images.ygoprodeck").And.Not.Contain("evil.test")
            .And.Not.Contain("data:image").And.Not.Contain("blob:"));
        CardInfoService next = new(storage, client, new TestTimeProvider(Now));
        CardInfo restored = await next.GetCardArtworkInfoAsync(1234);
        Assert.That(restored.ArtworkImageIds, Is.EqualTo(info.ArtworkImageIds));
        Assert.That(restored.SelectArtworkImageId(2222), Is.EqualTo(2222));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
    }

    [Test]
    public async Task AlternateQueryPasscodeRemainsDistinctFromCanonicalAndInternalIdentity() {
        TestLocalStorage storage = new(CreateCacheJson(Now, (5678, "Other")));
        RecordingHttpMessageHandler handler = new((_, _) => Task.FromResult(Response(HttpStatusCode.OK,
            "{\"data\":[" + ArtworkCardJson + "]}")));
        using HttpClient client = new(handler);
        CardInfoService service = new(storage, client, new TestTimeProvider(Now));
        CardInfo info = await service.GetCardArtworkInfoAsync(2222);
        Assert.That(info.Id, Is.EqualTo(2222));
        Assert.That(info.CanonicalCardId, Is.EqualTo(1234));
        Assert.That(info.SelectArtworkImageId(2222), Is.EqualTo(2222));
        string? url = await new CardArtworkService(service).GetArtworkUrlAsync(2222);
        Assert.That(url, Is.EqualTo(CardArtworkService.ArtworkOrigin + "/small/2222.jpg"));
        Assert.That(handler.Requests.Single(), Does.EndWith("?id=2222"));
    }

    [TestCase("")]
    [TestCase(",\"card_images\":null")]
    [TestCase(",\"card_images\":42")]
    [TestCase(",\"card_images\":[]")]
    [TestCase(",\"card_images\":[{\"id\":1,\"image_url_small\":\"https://evil.test/image.jpg\"}]")]
    public async Task AuthoritativeMissingOrMalformedArtworkPreservesCardAndDoesNotRefetch(string images) {
        TestLocalStorage storage = new(null);
        RecordingHttpMessageHandler handler = new((_, _) => Task.FromResult(Response(HttpStatusCode.OK,
            "{\"data\":[{\"id\":1234,\"name\":\"Spell\",\"type\":\"Spell Card\"" + images + "}]}")));
        using HttpClient client = new(handler);
        CardInfoService service = new(storage, client, new TestTimeProvider(Now));
        CardInfo info = await service.GetCardArtworkInfoAsync(1234);
        Assert.That(info.Type, Is.EqualTo("Spell Card"));
        Assert.That(info.ArtworkMetadataKnown, Is.True);
        Assert.That(info.SelectArtworkImageId(1234), Is.Null);
        CardArtworkService urls = new(service);
        Assert.That(await urls.GetArtworkUrlAsync(1234), Is.Null);
        CardInfoService next = new(storage, client, new TestTimeProvider(Now));
        Assert.That((await next.GetCardArtworkInfoAsync(1234)).ArtworkMetadataKnown, Is.True);
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
    }

    [Test]
    public async Task FreshExistingV2CacheEnrichesOnlyRequestedArtworkOnce() {
        TestLocalStorage storage = new(CreateCacheJson(Now, (1234, "Saved"), (5678, "Other")));
        RecordingHttpMessageHandler handler = new((_, _) => Task.FromResult(Response(HttpStatusCode.OK, "{\"data\":[" + ArtworkCardJson + "]}")));
        using HttpClient client = new(handler);
        CardInfoService service = new(storage, client, new TestTimeProvider(Now));
        Assert.That((await service.GetCardInfoAsync(1234)).Name, Is.EqualTo("Saved"));
        Assert.That(handler.Requests, Is.Empty);
        CardInfo[] results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => service.GetCardArtworkInfoAsync(1234)));
        Assert.That(results.All(info => info.ArtworkImageIds.Count == 3), Is.True);
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
        Assert.That(handler.Requests[0], Does.EndWith("?id=1234"));
        using JsonDocument doc = JsonDocument.Parse(storage.RawCache!);
        Assert.That(doc.RootElement.GetProperty("LastFullRefreshUtc").GetDateTimeOffset(), Is.EqualTo(Now));
        Assert.That((await service.GetCardInfoAsync(5678)).Name, Is.EqualTo("Other"));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
    }

    [Test]
    public async Task DisplayLookupsShareInFlightResolutionAndCacheUnavailableIds() {
        MockCardInfo info = new();
        CardArtworkService artwork = new(info);
        Task<string?> first = artwork.GetArtworkUrlAsync(1234);
        Task<string?> second = artwork.GetArtworkUrlAsync(1234);
        Assert.That(info.Calls, Is.EqualTo(1));
        info.Result.SetResult(new CardInfo { Id = 1234, CanonicalCardId = 1234,
            ArtworkImageIds = new[] {1234}, ArtworkMetadataKnown = true });
        Assert.That(await first, Is.EqualTo(CardArtworkService.ArtworkOrigin + "/small/1234.jpg"));
        Assert.That(await second, Is.EqualTo(await first));
        Assert.That(await artwork.GetArtworkUrlAsync(0), Is.Null);
        Assert.That(info.Calls, Is.EqualTo(1));
    }

    private sealed class MockCardInfo : ICardInfoService {
        public int Calls;
        public TaskCompletionSource<CardInfo> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IReadOnlyList<CardInfo>> SearchCardsAsync(string query) => throw new NotSupportedException();
        public Task<CardInfo> GetCardInfoAsync(int id) {
            Calls++;

            return Result.Task;
        }

        public Task<string> GetCardNameAsync(int id) => throw new NotSupportedException();

        public Task<IReadOnlyDictionary<string, CardInfo>> GetCardInfoByExactNamesAsync(IEnumerable<string> names) => throw new NotSupportedException();
    }

    private const string RichCardJson = """
        {"id":1234,"name":"Pendulum","type":"XYZ Pendulum Effect Monster","frameType":"xyz_pendulum",
         "race":"Warrior","attribute":"FIRE","level":4,"linkval":2,"scale":8,"archetype":"Vanquish Soul",
         "desc":"Not cached","card_prices":[{"price":"100"}],"card_images":[{"image_url":"not cached"}]}
        """;

    [Test]
    public async Task ExactNamesReuseRichCacheIncludingStaleEntriesWithoutNetwork() {
        TestLocalStorage storage = new(CreateCacheJson(Now.AddDays(-10), (1234, "Exact")));
        RecordingHttpMessageHandler handler = HandlerWithStatus(HttpStatusCode.ServiceUnavailable);
        using HttpClient client = new(handler);
        CardInfoService service = new(storage, client, new TestTimeProvider(Now));
        IReadOnlyDictionary<string, CardInfo> result = await service.GetCardInfoByExactNamesAsync(["Exact", "Exact"]);
        Assert.That(result["Exact"].Id, Is.EqualTo(1234));
        Assert.That(handler.Requests, Is.Empty);
        Assert.That(storage.WriteCount, Is.Zero);
    }

    [TestCase(1)]
    [TestCase(2)]
    public async Task ExactNamesEnrichIncompleteCacheDeduplicateEscapeAndPersist(int version) {
        const string name = "Ash Blossom & Joyous Spring";
        TestLocalStorage storage = new(JsonSerializer.Serialize(new {
            SchemaVersion = version, LastFullRefreshUtc = Now,
            Cards = new Dictionary<int, object> { [14558127] = new { Name = name } }
        }));
        RecordingHttpMessageHandler handler = new((_, _) => Task.FromResult(Response(HttpStatusCode.OK,
            "{\"data\":[{\"id\":14558127,\"name\":\"Ash Blossom & Joyous Spring\",\"type\":\"Tuner Monster\",\"level\":3}]}")));
        using HttpClient client = new(handler);
        CardInfoService service = new(storage, client, new TestTimeProvider(Now));
        IReadOnlyDictionary<string, CardInfo> result = await service.GetCardInfoByExactNamesAsync([name, name]);
        Assert.That(result[name].Id, Is.EqualTo(14558127));
        Assert.That(result[name].Type, Is.EqualTo("Tuner Monster"));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
        Assert.That(Uri.UnescapeDataString(new Uri(handler.Requests.Single()).Query), Is.EqualTo("?name=" + name));
        Assert.That(handler.Requests.Single(), Does.Not.Contain("fname=").And.Not.Contain("?id="));
        using JsonDocument document = JsonDocument.Parse(storage.RawCache!);
        Assert.That(document.RootElement.GetProperty("SchemaVersion").GetInt32(), Is.EqualTo(2));
        if (version == 1) {
            Assert.That(document.RootElement.GetProperty("LastFullRefreshUtc").ValueKind, Is.EqualTo(JsonValueKind.Null));
        }

        CardInfoService next = new(storage, client, new TestTimeProvider(Now));
        Assert.That((await next.GetCardInfoByExactNamesAsync([name]))[name], Is.EqualTo(result[name]));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
    }

    [TestCase("{\"data\":[{\"id\":1,\"name\":\"exact\",\"type\":\"Spell Card\"}]}")]
    [TestCase("{\"data\":[{\"id\":1,\"name\":\"Exact extra\",\"type\":\"Spell Card\"}]}")]
    [TestCase("{\"data\":[{\"id\":1,\"name\":\"Exact\"}]}")]
    [TestCase("{\"data\":[{\"id\":1,\"name\":\"Exact\",\"type\":\"Spell Card\"},{\"id\":2,\"name\":\"Exact\",\"type\":\"Spell Card\"}]}")]
    [TestCase("{\"data\":[]}")]
    [TestCase("{bad")]
    public async Task ExactNamesRejectNonmatchingAmbiguousIncompleteOrInvalidResponses(string response) {
        TestLocalStorage storage = new(null);
        RecordingHttpMessageHandler handler = new((_, _) => Task.FromResult(Response(HttpStatusCode.OK, response)));
        using HttpClient client = new(handler);
        CardInfoService service = new(storage, client, new TestTimeProvider(Now));
        Assert.That(await service.GetCardInfoByExactNamesAsync(["Exact", "Exact"]), Is.Empty);
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
        Assert.That(new Uri(handler.Requests.Single()).Query, Is.EqualTo("?name=Exact"));
        Assert.That(storage.WriteCount, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ExactNamesStorageFailuresDoNotLoseSuccessfulResolution(bool readFailure) {
        TestLocalStorage storage = new("{bad") { ThrowOnRead = readFailure, ThrowOnWrite = true };
        RecordingHttpMessageHandler handler = new((_, _) => Task.FromResult(Response(HttpStatusCode.OK,
            "{\"data\":[" + RichCardJson + "]}")));
        using HttpClient client = new(handler);
        CardInfoService service = new(storage, client, new TestTimeProvider(Now));
        Assert.That((await service.GetCardInfoByExactNamesAsync(["Pendulum"]))["Pendulum"].Id, Is.EqualTo(1234));
        Assert.That((await service.GetCardInfoByExactNamesAsync(["Pendulum"]))["Pendulum"].Type, Is.Not.Null);
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ExactNamesNetworkFailuresDoNotFailTheOtherNames(bool throws) {
        RecordingHttpMessageHandler handler = new((request, _) => {
            if (request.RequestUri!.Query.Contains("Missing")) {
                if (throws) {
                    throw new HttpRequestException("Offline");
                }

                return Task.FromResult(Response(HttpStatusCode.NotFound, "{}"));
            }
            return Task.FromResult(Response(HttpStatusCode.OK, "{\"data\":[" + RichCardJson + "]}"));
        });
        using HttpClient client = new(handler);
        CardInfoService service = new(new TestLocalStorage(null), client, new TestTimeProvider(Now));
        IReadOnlyDictionary<string, CardInfo> result = await service.GetCardInfoByExactNamesAsync(["Missing", "Pendulum"]);
        Assert.That(result.Keys, Is.EqualTo(new[] { "Pendulum" }));
        Assert.That(handler.Requests, Has.Length.EqualTo(2));
    }

    [Test]
    public async Task TargetedEndpointParsesReusableMetadata() {
        TestLocalStorage storage = new(null);
        RecordingHttpMessageHandler handler = new((_, _) => Task.FromResult(Response(HttpStatusCode.OK,
            "{\"data\":[" + RichCardJson + "]}")));
        using HttpClient client = new(handler);
        CardInfoService service = new(storage, client, new TestTimeProvider(Now));
        CardInfo info = await service.GetCardInfoAsync(1234);
        Assert.That(info, Is.EqualTo(new CardInfo { Id = 1234, Name = "Pendulum", Type = "XYZ Pendulum Effect Monster",
            FrameType = "xyz_pendulum", Race = "Warrior", Attribute = "FIRE", Level = 4, LinkVal = 2, Scale = 8, Archetype = "Vanquish Soul",
            CanonicalCardId = 1234, ArtworkMetadataKnown = true }));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
        Assert.That(handler.Requests[0], Does.EndWith("?id=1234"));
        Assert.That(storage.RawCache, Does.Not.Contain("Not cached").And.Not.Contain("card_prices").And.Not.Contain("image_url"));
        RecordingHttpMessageHandler nextHandler = HandlerWithStatus(HttpStatusCode.ServiceUnavailable);
        using HttpClient nextClient = new(nextHandler);
        CardInfoService next = new(storage, nextClient, new TestTimeProvider(Now));
        Assert.That(await next.GetCardInfoAsync(1234), Is.EqualTo(info));
        Assert.That(nextHandler.Requests, Has.Length.EqualTo(1));
        Assert.That(nextHandler.Requests.Single(), Does.EndWith("cardinfo.php?id=1234"));
    }

    [Test]
    public async Task FreshV1NameCacheRequiresMetadataRefreshAndUpgradesToV2() {
        TestLocalStorage storage = new(CreateV1CacheJson());
        RecordingHttpMessageHandler handler = new((_, _) => Task.FromResult(Response(HttpStatusCode.OK,
            "{\"data\":[" + RichCardJson + "]}")));
        using HttpClient client = new(handler);
        CardInfoService service = new(storage, client, new TestTimeProvider(Now));
        Assert.That((await service.GetCardInfoAsync(1234)).Attribute, Is.EqualTo("FIRE"));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
        AssertPersistedCache(storage.RawCache, null, (1234, "Pendulum"), (5678, "Other old name"));
    }

    [Test]
    public async Task FailedV1EnrichmentRetainsCachedNameAndNeverInventsProperties() {
        string original = CreateV1CacheJson();
        TestLocalStorage storage = new(original);
        RecordingHttpMessageHandler handler = HandlerWithStatus(HttpStatusCode.ServiceUnavailable);
        using HttpClient client = new(handler);
        CardInfoService service = new(storage, client, new TestTimeProvider(Now));
        CardInfo info = await service.GetCardInfoAsync(1234);
        Assert.That(info.Name, Is.EqualTo("Old name"));
        Assert.That(info.Id, Is.EqualTo(1234));
        Assert.That(CardPropertyProvider.GetCategories(info), Is.Empty);
        Assert.That(storage.RawCache, Is.EqualTo(original));
        Assert.That(await service.GetCardInfoAsync(1234), Is.EqualTo(info));
        Assert.That(handler.Requests, Has.Length.EqualTo(1), "Legacy metadata uses one targeted lookup.");
    }

    [Test]
    public async Task PerCardV1EnrichmentDoesNotClaimAFullMetadataRefresh() {
        TestLocalStorage storage = new(CreateV1CacheJson());
        RecordingHttpMessageHandler handler = new((request, _) => Task.FromResult(
            request.RequestUri!.Query.Length == 0 ? Response(HttpStatusCode.ServiceUnavailable, "{}")
                : Response(HttpStatusCode.OK, "{\"data\":[" + RichCardJson + "]}")));
        using HttpClient client = new(handler);
        CardInfoService service = new(storage, client, new TestTimeProvider(Now));
        Assert.That((await service.GetCardInfoAsync(1234)).Scale, Is.EqualTo(8));
        using JsonDocument document = JsonDocument.Parse(storage.RawCache!);
        Assert.That(document.RootElement.GetProperty("SchemaVersion").GetInt32(), Is.EqualTo(2));
        Assert.That(document.RootElement.GetProperty("LastFullRefreshUtc").ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(document.RootElement.GetProperty("Cards").GetProperty("5678").GetProperty("Name").GetString(), Is.EqualTo("Other old name"));
        RecordingHttpMessageHandler nextHandler = HandlerWithTargetedCards(200, (1234, "Current"));
        using HttpClient nextClient = new(nextHandler);
        CardInfoService nextService = new(storage, nextClient, new TestTimeProvider(Now));
        Assert.That((await nextService.GetCardInfoAsync(1234)).Name, Is.EqualTo("Current"));
        Assert.That(nextHandler.Requests, Has.Length.EqualTo(1));
    }

    [Test]
    public async Task OptionalMalformedFieldsDoNotDiscardReliableNameOrType() {
        RecordingHttpMessageHandler handler = new((_, _) => Task.FromResult(Response(HttpStatusCode.OK,
            "{\"data\":[{\"id\":\"bad\",\"name\":\"\"},{\"id\":1234,\"name\":\"Spell\",\"type\":\"Spell Card\",\"race\":\"Quick-Play\",\"level\":\"bad\",\"scale\":{},\"attribute\":42}]}")));
        using HttpClient client = new(handler);
        CardInfoService service = new(new TestLocalStorage(null), client, new TestTimeProvider(Now));
        CardInfo info = await service.GetCardInfoAsync(1234);
        Assert.That(info.Name, Is.EqualTo("Spell"));
        Assert.That(info.Level, Is.Null);
        Assert.That(info.Attribute, Is.Null);
        Assert.That(CardPropertyProvider.GetCategories(info).Select(c => c.Name), Is.EqualTo(new[] { "Spell", "Quick-Play Spell" }));
    }

    private static string CreateV1CacheJson() => JsonSerializer.Serialize(new {
        SchemaVersion = 1, LastFullRefreshUtc = Now,
        Cards = new Dictionary<string, object> { ["1234"] = new { Name = "Old name" }, ["5678"] = new { Name = "Other old name" } }
    });

    [Test]
    public async Task ServiceConstructionDoesNotStartAnApiRequestForSavedSessions() {
        TestLocalStorage storage = new(null);
        RecordingHttpMessageHandler handler = HandlerWithTargetedCards(200, (1234, "Imported"));
        using HttpClient client = new(handler);
        CardInfoService service = new(storage, client, new TestTimeProvider(Now));
        Assert.That(handler.Requests, Is.Empty);
        Assert.That((await service.GetCardInfoAsync(1234)).Name, Is.EqualTo("Imported"));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
    }

    [Test]
    public async Task SingleCardLookupKeepsQueriedPasscodeAndReliableNameFallback() {
        TestLocalStorage storage = new(CreateCacheJson(Now, (5678, "Other")));
        RecordingHttpMessageHandler handler = new((_, _) => Task.FromResult(Response(HttpStatusCode.OK,
            "{\"data\":[{\"id\":4321,\"name\":\"Alternate passcode\",\"type\":\"Normal Monster\"}]}")));
        using HttpClient client = new(handler);
        CardInfoService service = new(storage, client, new TestTimeProvider(Now));
        CardInfo info = await service.GetCardInfoAsync(1234);
        Assert.That(info.Id, Is.EqualTo(1234));
        Assert.That(info.Name, Is.EqualTo("Alternate passcode"));
        Assert.That(await service.GetCardNameAsync(1234), Is.EqualTo(info.Name));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
    }

    [Test]
    public async Task FreshCurrentCache_ReturnsCachedNameWithoutApiRequest() {
        TestLocalStorage storage = new(CreateCacheJson(Now.AddDays(-7).AddHours(1), (1234, "Blue-Eyes White Dragon")));
        RecordingHttpMessageHandler handler = new((_, _) => Task.FromResult(Response(HttpStatusCode.InternalServerError, "{}")));

        using HttpClient httpClient = new(handler);
        CardInfoService service = new(storage, httpClient, new TestTimeProvider(Now));

        string result = await service.GetCardNameAsync(1234);

        Assert.That(result, Is.EqualTo("Blue-Eyes White Dragon"));
        Assert.That(handler.Requests, Is.Empty);
    }

    [Test]
    public async Task ExpiredCache_RefreshesOnlyRequestedCardAndPersistsCurrentEnvelope() {
        TestLocalStorage storage = new(CreateCacheJson(Now.AddDays(-7), (1234, "Old Name")));
        RecordingHttpMessageHandler handler = HandlerWithTargetedCards(200, (1234, "Updated Name"), (5678, "Dark Magician"));

        using HttpClient httpClient = new(handler);
        CardInfoService service = new(storage, httpClient, new TestTimeProvider(Now));

        string result = await service.GetCardNameAsync(1234);

        Assert.That(result, Is.EqualTo("Updated Name"));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
        AssertPersistedCache(storage.RawCache, Now.AddDays(-7), (1234, "Updated Name"));
        Assert.That(handler.Requests.Single(), Does.EndWith("cardinfo.php?id=1234"));
    }

    [Test]
    public async Task ExpiredCacheAndFailedTargetedLookup_RetainsStaleNameAndTimestamp() {
        DateTimeOffset oldTimestamp = Now.AddDays(-10);
        string originalJson = CreateCacheJson(oldTimestamp, (1234, "Stale Name"));
        TestLocalStorage storage = new(originalJson);
        RecordingHttpMessageHandler handler = HandlerWithStatus(HttpStatusCode.ServiceUnavailable);

        using HttpClient httpClient = new(handler);
        CardInfoService service = new(storage, httpClient, new TestTimeProvider(Now));

        string result = await service.GetCardNameAsync(1234);

        Assert.That(result, Is.EqualTo("Stale Name"));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
        Assert.That(storage.RawCache, Is.EqualTo(originalJson));
        Assert.That(storage.WriteCount, Is.Zero);
    }

    [Test]
    public async Task ExpiredCacheAndEmptyTargetedResponse_RetainsStaleSnapshot() {
        string originalJson = CreateCacheJson(Now.AddDays(-9), (1234, "Stale Name"));
        TestLocalStorage storage = new(originalJson);
        RecordingHttpMessageHandler handler = new((_, _) => Task.FromResult(Response(HttpStatusCode.OK, "{\"data\":[]}")));

        using HttpClient httpClient = new(handler);
        CardInfoService service = new(storage, httpClient, new TestTimeProvider(Now));

        Assert.That(await service.GetCardNameAsync(1234), Is.EqualTo("Stale Name"));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
        Assert.That(storage.RawCache, Is.EqualTo(originalJson));
        Assert.That(storage.WriteCount, Is.Zero);
    }

    [Test]
    public async Task LegacyCache_UpgradesAfterSuccessfulTargetedLookup() {
        TestLocalStorage storage = new("{\"1234\":\"Legacy Name\"}");
        RecordingHttpMessageHandler handler = HandlerWithTargetedCards(200, (1234, "Refreshed Name"));

        using HttpClient httpClient = new(handler);
        CardInfoService service = new(storage, httpClient, new TestTimeProvider(Now));

        string result = await service.GetCardNameAsync(1234);

        Assert.That(result, Is.EqualTo("Refreshed Name"));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
        AssertPersistedCache(storage.RawCache, null, (1234, "Refreshed Name"));
    }

    [Test]
    public async Task LegacyCacheAndFailedTargetedLookup_RemainsUsableForKnownCard() {
        TestLocalStorage storage = new("{\"1234\":\"Legacy Name\"}");
        RecordingHttpMessageHandler handler = HandlerWithStatus(HttpStatusCode.ServiceUnavailable);

        using HttpClient httpClient = new(handler);
        CardInfoService service = new(storage, httpClient, new TestTimeProvider(Now));

        string result = await service.GetCardNameAsync(1234);

        Assert.That(result, Is.EqualTo("Legacy Name"));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
        Assert.That(storage.WriteCount, Is.Zero);
    }

    [Test]
    public async Task MalformedCache_DoesNotBreakImportAndAttemptsFreshFetch() {
        TestLocalStorage storage = new("{malformed json");
        RecordingHttpMessageHandler handler = HandlerWithTargetedCards(200, (5678, "Dark Magician"));

        using HttpClient httpClient = new(handler);
        CardInfoService service = new(storage, httpClient, new TestTimeProvider(Now));

        string result = await service.GetCardNameAsync(5678);

        Assert.That(result, Is.EqualTo("Dark Magician"));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
        AssertPersistedCache(storage.RawCache, null, (5678, "Dark Magician"));
    }

    [TestCase(null)]
    [TestCase("{}")]
    public async Task MissingOrEmptyCache_FetchesOnlyRequestedCard(string? rawCache) {
        TestLocalStorage storage = new(rawCache);
        RecordingHttpMessageHandler handler = HandlerWithTargetedCards(200, (5678, "Dark Magician"));

        using HttpClient httpClient = new(handler);
        CardInfoService service = new(storage, httpClient, new TestTimeProvider(Now));

        string result = await service.GetCardNameAsync(5678);

        Assert.That(result, Is.EqualTo("Dark Magician"));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
        AssertPersistedCache(storage.RawCache, null, (5678, "Dark Magician"));
    }

    [Test]
    public async Task UnsupportedCacheVersion_IsDiscardedAndRefreshed() {
        TestLocalStorage storage = new("{\"SchemaVersion\":99,\"Cards\":{\"1234\":{\"Name\":\"Future Name\"}}}");
        RecordingHttpMessageHandler handler = HandlerWithTargetedCards(200, (5678, "Dark Magician"));

        using HttpClient httpClient = new(handler);
        CardInfoService service = new(storage, httpClient, new TestTimeProvider(Now));

        string result = await service.GetCardNameAsync(5678);

        Assert.That(result, Is.EqualTo("Dark Magician"));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
        AssertPersistedCache(storage.RawCache, null, (5678, "Dark Magician"));
    }

    [Test]
    public async Task MalformedEnvelopeWithFreshTimestamp_IsDiscardedAndRefreshed() {
        TestLocalStorage storage = new("{\"SchemaVersion\":1,\"LastFullRefreshUtc\":\"2026-10-01T09:00:00Z\",\"Cards\":{\"not-a-card-id\":{\"Name\":\"Unusable\"}}}");
        RecordingHttpMessageHandler handler = HandlerWithTargetedCards(200, (5678, "Dark Magician"));

        using HttpClient httpClient = new(handler);
        CardInfoService service = new(storage, httpClient, new TestTimeProvider(Now));

        Assert.That(await service.GetCardNameAsync(5678), Is.EqualTo("Dark Magician"));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
        AssertPersistedCache(storage.RawCache, null, (5678, "Dark Magician"));
    }

    [Test]
    public async Task CacheMiss_UsesSingleCardEndpointAndPersistsMetadataEnvelope() {
        DateTimeOffset refreshedAt = Now.AddDays(-2);
        TestLocalStorage storage = new(CreateCacheJson(refreshedAt, (1234, "Known Card")));
        RecordingHttpMessageHandler handler = new((request, _) => Task.FromResult(
            request.RequestUri!.Query == "?id=5678"
                ? Response(HttpStatusCode.OK, "{\"data\":[{\"id\":5678,\"name\":\"Dark Magician\",\"type\":\"Normal Monster\"}]}")
                : Response(HttpStatusCode.InternalServerError, "{}")));

        using HttpClient httpClient = new(handler);
        CardInfoService service = new(storage, httpClient, new TestTimeProvider(Now));

        string result = await service.GetCardNameAsync(5678);

        Assert.That(result, Is.EqualTo("Dark Magician"));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
        AssertPersistedCache(storage.RawCache, refreshedAt, (1234, "Known Card"), (5678, "Dark Magician"));
    }

    [Test]
    public async Task SingleCardFetch_DoesNotAdvanceFullRefreshTimestamp() {
        DateTimeOffset refreshedAt = Now.AddDays(-6);
        TestLocalStorage storage = new(CreateCacheJson(refreshedAt, (1234, "Known Card")));
        RecordingHttpMessageHandler handler = new((request, _) => Task.FromResult(
            request.RequestUri!.Query == "?id=5678"
                ? Response(HttpStatusCode.OK, "{\"data\":[{\"id\":5678,\"name\":\"New Card\"}]}")
                : Response(HttpStatusCode.InternalServerError, "{}")));

        using HttpClient httpClient = new(handler);
        CardInfoService service = new(storage, httpClient, new TestTimeProvider(Now));

        string result = await service.GetCardNameAsync(5678);

        Assert.That(result, Is.EqualTo("New Card"));
        AssertPersistedCache(storage.RawCache, refreshedAt, (1234, "Known Card"), (5678, "New Card"));
    }

    [Test]
    public async Task SingleCardFetchAfterLegacyCache_PersistsUnknownTimestampAndNextInstanceUsesTargetedLookup() {
        TestLocalStorage storage = new("{\"1234\":\"Legacy Name\"}");
        RecordingHttpMessageHandler firstHandler = new((request, _) => Task.FromResult(
            request.RequestUri!.Query == "?id=5678"
                ? Response(HttpStatusCode.OK, "{\"data\":[{\"id\":5678,\"name\":\"New Card\"}]}")
                : Response(HttpStatusCode.ServiceUnavailable, "{}")));

        using (HttpClient firstHttpClient = new(firstHandler)) {
            CardInfoService firstService = new(storage, firstHttpClient, new TestTimeProvider(Now));
            Assert.That(await firstService.GetCardNameAsync(5678), Is.EqualTo("New Card"));
        }

        using (JsonDocument document = JsonDocument.Parse(storage.RawCache!)) {
            JsonElement root = document.RootElement;
            Assert.That(root.GetProperty("SchemaVersion").GetInt32(), Is.EqualTo(2));
            Assert.That(root.GetProperty("LastFullRefreshUtc").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(root.GetProperty("Cards").GetProperty("1234").GetProperty("Name").GetString(), Is.EqualTo("Legacy Name"));
            Assert.That(root.GetProperty("Cards").GetProperty("5678").GetProperty("Name").GetString(), Is.EqualTo("New Card"));
        }

        RecordingHttpMessageHandler nextHandler = HandlerWithTargetedCards(200, (1234, "Refreshed Name"));
        using HttpClient nextHttpClient = new(nextHandler);
        CardInfoService nextService = new(storage, nextHttpClient, new TestTimeProvider(Now));

        Assert.That(await nextService.GetCardNameAsync(1234), Is.EqualTo("Refreshed Name"));
        Assert.That(nextHandler.Requests, Has.Length.EqualTo(1));
    }

    [Test]
    public async Task TargetedHttpFailure_FallsBackToNumericId() {
        TestLocalStorage storage = new(null);
        RecordingHttpMessageHandler handler = HandlerWithStatus(HttpStatusCode.ServiceUnavailable);

        using HttpClient httpClient = new(handler);
        CardInfoService service = new(storage, httpClient, new TestTimeProvider(Now));

        string result = await service.GetCardNameAsync(9999);

        Assert.That(result, Is.EqualTo("9999"));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
    }

    [Test]
    public async Task LocalStorageReadAndWriteFailures_DoNotBreakImport() {
        TestLocalStorage storage = new(null) { ThrowOnRead = true, ThrowOnWrite = true };
        RecordingHttpMessageHandler handler = HandlerWithTargetedCards(200, (5678, "Dark Magician"));

        using HttpClient httpClient = new(handler);
        CardInfoService service = new(storage, httpClient, new TestTimeProvider(Now));

        string result = await service.GetCardNameAsync(5678);

        Assert.That(result, Is.EqualTo("Dark Magician"));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
        Assert.That(storage.WriteCount, Is.Zero);
    }

    [Test]
    public async Task ParallelStartupLookups_UseOnlyTargetedRequestsForRequestedCards() {
        TestLocalStorage storage = new(CreateCacheJson(Now.AddDays(-8), (1234, "Old A"), (5678, "Old B")));
        RecordingHttpMessageHandler handler = HandlerWithTargetedCards(200, (1234, "New A"), (5678, "New B"));

        using HttpClient httpClient = new(handler);
        CardInfoService service = new(storage, httpClient, new TestTimeProvider(Now));

        string[] results = await Task.WhenAll(service.GetCardNameAsync(1234), service.GetCardNameAsync(5678));

        Assert.That(results, Is.EqualTo(new[] { "New A", "New B" }));
        Assert.That(handler.Requests.Select(request => new Uri(request).Query),
            Is.EquivalentTo(new[] { "?id=1234", "?id=5678" }));
    }

    private static RecordingHttpMessageHandler HandlerWithTargetedCards(int statusCode, params (int Id, string Name)[] cards) =>
        new((request, _) => {
            string query = request.RequestUri!.Query;
            (int Id, string Name)[] responseCards = cards
                .Where(card => query == $"?id={card.Id}")
                .ToArray();
            return Task.FromResult(Response((HttpStatusCode)statusCode, CreateBulkJson(responseCards)));
        });

    private static RecordingHttpMessageHandler HandlerWithStatus(HttpStatusCode statusCode) =>
        new((_, _) => Task.FromResult(Response(statusCode, "{}")));

    private static HttpResponseMessage Response(HttpStatusCode statusCode, string json) => new() {
        StatusCode = statusCode,
        Content = new StringContent(json)
    };

    private static string CreateBulkJson(params (int Id, string Name)[] cards) =>
        JsonSerializer.Serialize(new {
            data = cards.Select(card => new { id = card.Id, name = card.Name, type = "Effect Monster" })
        });

    private static string CreateLocalCatalogJson(IEnumerable<CardInfo> cards) =>
        JsonSerializer.Serialize(new { version = 1, cards = cards.ToArray() }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static string CreateCacheJson(DateTimeOffset refreshedAt, params (int Id, string Name)[] cards) =>
        JsonSerializer.Serialize(new {
            SchemaVersion = 2,
            LastFullRefreshUtc = refreshedAt,
            Cards = cards.ToDictionary(card => card.Id.ToString(), card => new { Id = card.Id, Name = card.Name, Type = "Effect Monster" })
        });

    private static void AssertPersistedCache(string? json, DateTimeOffset? expectedTimestamp, params (int Id, string Name)[] expectedCards) {
        Assert.That(json, Is.Not.Null);
        using JsonDocument document = JsonDocument.Parse(json!);
        JsonElement root = document.RootElement;
        Assert.That(root.GetProperty("SchemaVersion").GetInt32(), Is.EqualTo(2));
        JsonElement timestamp = root.GetProperty("LastFullRefreshUtc");
        if (expectedTimestamp is DateTimeOffset expected) {
            Assert.That(timestamp.GetDateTimeOffset(), Is.EqualTo(expected));
        }
        else {
            Assert.That(timestamp.ValueKind, Is.EqualTo(JsonValueKind.Null));
        }
        JsonElement cards = root.GetProperty("Cards");
        Assert.That(cards.EnumerateObject().Count(), Is.EqualTo(expectedCards.Length));
        foreach ((int id, string name) in expectedCards) {
            Assert.That(cards.GetProperty(id.ToString()).GetProperty("Name").GetString(), Is.EqualTo(name));
        }
    }

    private sealed class TestLocalStorage(string? rawCache) : ILocalStorageService {
        public string? RawCache { get; private set; } = rawCache;
        public bool ThrowOnRead { get; init; }
        public bool ThrowOnWrite { get; init; }
        public int WriteCount { get; private set; }

        public Task<T?> GetItemAsync<T>(string key) =>
            Task.FromResult(RawCache is null ? default : JsonSerializer.Deserialize<T>(RawCache));

        public Task<string?> GetRawItemAsync(string key) {
            if (ThrowOnRead) {
                throw new InvalidOperationException("Storage read failed.");
            }

            return Task.FromResult(RawCache);
        }

        public Task SetItemAsync<T>(string key, T value) {
            if (ThrowOnWrite) {
                throw new InvalidOperationException("Storage write failed.");
            }

            RawCache = JsonSerializer.Serialize(value);
            WriteCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class TestTimeProvider(DateTimeOffset now) : TimeProvider {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory
    ) : HttpMessageHandler {
        private readonly ConcurrentQueue<string> _requests = new();

        public string[] Requests => _requests.ToArray();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            _requests.Enqueue(request.RequestUri!.ToString());
            return responseFactory(request, cancellationToken);
        }
    }
}
