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
        var storage = new TestLocalStorage(null);
        var handler = new RecordingHttpMessageHandler((_, _) => Task.FromResult(Response(HttpStatusCode.OK,
            "{\"data\":[" + ArtworkCardJson + "]}")));
        using var client = new HttpClient(handler);
        var service = new CardInfoService(storage, client, new TestTimeProvider(Now));
        var info = await service.GetCardArtworkInfoAsync(1234);
        Assert.That(info.ArtworkImageIds, Is.EqualTo(new[] {2222, 1234, 100000101}));
        Assert.That(info.CanonicalCardId, Is.EqualTo(1234));
        Assert.That(info.SelectArtworkImageId(1234), Is.EqualTo(1234));
        Assert.That(info.SelectArtworkImageId(2222), Is.EqualTo(2222));
        Assert.That(info.SelectArtworkImageId(5555), Is.EqualTo(1234));
        Assert.That((info with { CanonicalCardId = 9999 }).SelectArtworkImageId(5555), Is.EqualTo(2222));
        Assert.That(storage.RawCache, Does.Not.Contain("images.ygoprodeck").And.Not.Contain("evil.test")
            .And.Not.Contain("data:image").And.Not.Contain("blob:"));
        var next = new CardInfoService(storage, client, new TestTimeProvider(Now));
        var restored = await next.GetCardArtworkInfoAsync(1234);
        Assert.That(restored.ArtworkImageIds, Is.EqualTo(info.ArtworkImageIds));
        Assert.That(restored.SelectArtworkImageId(2222), Is.EqualTo(2222));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
    }

    [Test]
    public async Task AlternateQueryPasscodeRemainsDistinctFromCanonicalAndInternalIdentity() {
        var storage = new TestLocalStorage(CreateCacheJson(Now, (5678, "Other")));
        var handler = new RecordingHttpMessageHandler((_, _) => Task.FromResult(Response(HttpStatusCode.OK,
            "{\"data\":[" + ArtworkCardJson + "]}")));
        using var client = new HttpClient(handler);
        var service = new CardInfoService(storage, client, new TestTimeProvider(Now));
        var info = await service.GetCardArtworkInfoAsync(2222);
        Assert.That(info.Id, Is.EqualTo(2222));
        Assert.That(info.CanonicalCardId, Is.EqualTo(1234));
        Assert.That(info.SelectArtworkImageId(2222), Is.EqualTo(2222));
        var url = await new CardArtworkService(service).GetArtworkUrlAsync(2222);
        Assert.That(url, Is.EqualTo(CardArtworkService.ArtworkOrigin + "/small/2222.jpg"));
        Assert.That(handler.Requests.Single(), Does.EndWith("?id=2222"));
    }

    [TestCase("")]
    [TestCase(",\"card_images\":null")]
    [TestCase(",\"card_images\":42")]
    [TestCase(",\"card_images\":[]")]
    [TestCase(",\"card_images\":[{\"id\":1,\"image_url_small\":\"https://evil.test/image.jpg\"}]")]
    public async Task AuthoritativeMissingOrMalformedArtworkPreservesCardAndDoesNotRefetch(string images) {
        var storage = new TestLocalStorage(null);
        var handler = new RecordingHttpMessageHandler((_, _) => Task.FromResult(Response(HttpStatusCode.OK,
            "{\"data\":[{\"id\":1234,\"name\":\"Spell\",\"type\":\"Spell Card\"" + images + "}]}")));
        using var client = new HttpClient(handler);
        var service = new CardInfoService(storage, client, new TestTimeProvider(Now));
        var info = await service.GetCardArtworkInfoAsync(1234);
        Assert.That(info.Type, Is.EqualTo("Spell Card"));
        Assert.That(info.ArtworkMetadataKnown, Is.True);
        Assert.That(info.SelectArtworkImageId(1234), Is.Null);
        var urls = new CardArtworkService(service);
        Assert.That(await urls.GetArtworkUrlAsync(1234), Is.Null);
        var next = new CardInfoService(storage, client, new TestTimeProvider(Now));
        Assert.That((await next.GetCardArtworkInfoAsync(1234)).ArtworkMetadataKnown, Is.True);
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
    }

    [Test]
    public async Task FreshExistingV2CacheEnrichesOnlyRequestedArtworkOnce() {
        var storage = new TestLocalStorage(CreateCacheJson(Now, (1234, "Saved"), (5678, "Other")));
        var handler = new RecordingHttpMessageHandler((_, _) => Task.FromResult(Response(HttpStatusCode.OK, "{\"data\":[" + ArtworkCardJson + "]}")));
        using var client = new HttpClient(handler);
        var service = new CardInfoService(storage, client, new TestTimeProvider(Now));
        Assert.That((await service.GetCardInfoAsync(1234)).Name, Is.EqualTo("Saved"));
        Assert.That(handler.Requests, Is.Empty);
        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => service.GetCardArtworkInfoAsync(1234)));
        Assert.That(results.All(info => info.ArtworkImageIds.Count == 3), Is.True);
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
        Assert.That(handler.Requests[0], Does.EndWith("?id=1234"));
        using var doc = JsonDocument.Parse(storage.RawCache!);
        Assert.That(doc.RootElement.GetProperty("LastFullRefreshUtc").GetDateTimeOffset(), Is.EqualTo(Now));
        Assert.That((await service.GetCardInfoAsync(5678)).Name, Is.EqualTo("Other"));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
    }

    [Test]
    public async Task DisplayLookupsShareInFlightResolutionAndCacheUnavailableIds() {
        var info = new MockCardInfo();
        var artwork = new CardArtworkService(info);
        var first = artwork.GetArtworkUrlAsync(1234);
        var second = artwork.GetArtworkUrlAsync(1234);
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
        public Task<CardInfo> GetCardInfoAsync(int id) { Calls++; return Result.Task; }
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
        var storage = new TestLocalStorage(CreateCacheJson(Now.AddDays(-10), (1234, "Exact")));
        var handler = HandlerWithStatus(HttpStatusCode.ServiceUnavailable);
        using var client = new HttpClient(handler);
        var service = new CardInfoService(storage, client, new TestTimeProvider(Now));
        var result = await service.GetCardInfoByExactNamesAsync(["Exact", "Exact"]);
        Assert.That(result["Exact"].Id, Is.EqualTo(1234));
        Assert.That(handler.Requests, Is.Empty);
        Assert.That(storage.WriteCount, Is.Zero);
    }

    [TestCase(1)]
    [TestCase(2)]
    public async Task ExactNamesEnrichIncompleteCacheDeduplicateEscapeAndPersist(int version) {
        const string name = "Ash Blossom & Joyous Spring";
        var storage = new TestLocalStorage(JsonSerializer.Serialize(new {
            SchemaVersion = version, LastFullRefreshUtc = Now,
            Cards = new Dictionary<int, object> { [14558127] = new { Name = name } }
        }));
        var handler = new RecordingHttpMessageHandler((_, _) => Task.FromResult(Response(HttpStatusCode.OK,
            "{\"data\":[{\"id\":14558127,\"name\":\"Ash Blossom & Joyous Spring\",\"type\":\"Tuner Monster\",\"level\":3}]}")));
        using var client = new HttpClient(handler);
        var service = new CardInfoService(storage, client, new TestTimeProvider(Now));
        var result = await service.GetCardInfoByExactNamesAsync([name, name]);
        Assert.That(result[name].Id, Is.EqualTo(14558127));
        Assert.That(result[name].Type, Is.EqualTo("Tuner Monster"));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
        Assert.That(Uri.UnescapeDataString(new Uri(handler.Requests.Single()).Query), Is.EqualTo("?name=" + name));
        Assert.That(handler.Requests.Single(), Does.Not.Contain("fname=").And.Not.Contain("?id="));
        using var document = JsonDocument.Parse(storage.RawCache!);
        Assert.That(document.RootElement.GetProperty("SchemaVersion").GetInt32(), Is.EqualTo(2));
        if (version == 1)
            Assert.That(document.RootElement.GetProperty("LastFullRefreshUtc").ValueKind, Is.EqualTo(JsonValueKind.Null));
        var next = new CardInfoService(storage, client, new TestTimeProvider(Now));
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
        var storage = new TestLocalStorage(null);
        var handler = new RecordingHttpMessageHandler((_, _) => Task.FromResult(Response(HttpStatusCode.OK, response)));
        using var client = new HttpClient(handler);
        var service = new CardInfoService(storage, client, new TestTimeProvider(Now));
        Assert.That(await service.GetCardInfoByExactNamesAsync(["Exact", "Exact"]), Is.Empty);
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
        Assert.That(new Uri(handler.Requests.Single()).Query, Is.EqualTo("?name=Exact"));
        Assert.That(storage.WriteCount, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ExactNamesStorageFailuresDoNotLoseSuccessfulResolution(bool readFailure) {
        var storage = new TestLocalStorage("{bad") { ThrowOnRead = readFailure, ThrowOnWrite = true };
        var handler = new RecordingHttpMessageHandler((_, _) => Task.FromResult(Response(HttpStatusCode.OK,
            "{\"data\":[" + RichCardJson + "]}")));
        using var client = new HttpClient(handler);
        var service = new CardInfoService(storage, client, new TestTimeProvider(Now));
        Assert.That((await service.GetCardInfoByExactNamesAsync(["Pendulum"]))["Pendulum"].Id, Is.EqualTo(1234));
        Assert.That((await service.GetCardInfoByExactNamesAsync(["Pendulum"]))["Pendulum"].Type, Is.Not.Null);
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ExactNamesNetworkFailuresDoNotFailTheOtherNames(bool throws) {
        var handler = new RecordingHttpMessageHandler((request, _) => {
            if (request.RequestUri!.Query.Contains("Missing")) {
                if (throws) throw new HttpRequestException("Offline");
                return Task.FromResult(Response(HttpStatusCode.NotFound, "{}"));
            }
            return Task.FromResult(Response(HttpStatusCode.OK, "{\"data\":[" + RichCardJson + "]}"));
        });
        using var client = new HttpClient(handler);
        var service = new CardInfoService(new TestLocalStorage(null), client, new TestTimeProvider(Now));
        var result = await service.GetCardInfoByExactNamesAsync(["Missing", "Pendulum"]);
        Assert.That(result.Keys, Is.EqualTo(new[] { "Pendulum" }));
        Assert.That(handler.Requests, Has.Length.EqualTo(2));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task BulkAndSingleEndpointsParseTheSameReusableMetadata(bool single) {
        var storage = new TestLocalStorage(single ? CreateCacheJson(Now, (5678, "Other")) : null);
        var handler = new RecordingHttpMessageHandler((_, _) => Task.FromResult(Response(HttpStatusCode.OK,
            "{\"data\":[" + RichCardJson + "]}")));
        using var client = new HttpClient(handler);
        var service = new CardInfoService(storage, client, new TestTimeProvider(Now));
        var info = await service.GetCardInfoAsync(1234);
        Assert.That(info, Is.EqualTo(new CardInfo { Id = 1234, Name = "Pendulum", Type = "XYZ Pendulum Effect Monster",
            FrameType = "xyz_pendulum", Race = "Warrior", Attribute = "FIRE", Level = 4, LinkVal = 2, Scale = 8, Archetype = "Vanquish Soul",
            CanonicalCardId = 1234, ArtworkMetadataKnown = true }));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
        Assert.That(handler.Requests[0].EndsWith(single ? "?id=1234" : "cardinfo.php"), Is.True);
        Assert.That(storage.RawCache, Does.Not.Contain("Not cached").And.Not.Contain("card_prices").And.Not.Contain("image_url"));
        var nextHandler = HandlerWithStatus(HttpStatusCode.ServiceUnavailable);
        using var nextClient = new HttpClient(nextHandler);
        var next = new CardInfoService(storage, nextClient, new TestTimeProvider(Now));
        Assert.That(await next.GetCardInfoAsync(1234), Is.EqualTo(info));
        Assert.That(nextHandler.Requests, Is.Empty);
    }

    [Test]
    public async Task FreshV1NameCacheRequiresMetadataRefreshAndUpgradesToV2() {
        var storage = new TestLocalStorage(CreateV1CacheJson());
        var handler = new RecordingHttpMessageHandler((_, _) => Task.FromResult(Response(HttpStatusCode.OK,
            "{\"data\":[" + RichCardJson + "]}")));
        using var client = new HttpClient(handler);
        var service = new CardInfoService(storage, client, new TestTimeProvider(Now));
        Assert.That((await service.GetCardInfoAsync(1234)).Attribute, Is.EqualTo("FIRE"));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
        AssertPersistedCache(storage.RawCache, Now, (1234, "Pendulum"));
    }

    [Test]
    public async Task FailedV1RefreshRetainsCachedNameAndNeverInventsProperties() {
        var original = CreateV1CacheJson();
        var storage = new TestLocalStorage(original);
        var handler = HandlerWithStatus(HttpStatusCode.ServiceUnavailable);
        using var client = new HttpClient(handler);
        var service = new CardInfoService(storage, client, new TestTimeProvider(Now));
        var info = await service.GetCardInfoAsync(1234);
        Assert.That(info.Name, Is.EqualTo("Old name"));
        Assert.That(info.Id, Is.EqualTo(1234));
        Assert.That(CardPropertyProvider.GetCategories(info), Is.Empty);
        Assert.That(storage.RawCache, Is.EqualTo(original));
        Assert.That(await service.GetCardInfoAsync(1234), Is.EqualTo(info));
        Assert.That(handler.Requests, Has.Length.EqualTo(2), "One bulk attempt and one per-card attempt shared by later lookups.");
    }

    [Test]
    public async Task PerCardV1EnrichmentDoesNotClaimAFullMetadataRefresh() {
        var storage = new TestLocalStorage(CreateV1CacheJson());
        var handler = new RecordingHttpMessageHandler((request, _) => Task.FromResult(
            request.RequestUri!.Query.Length == 0 ? Response(HttpStatusCode.ServiceUnavailable, "{}")
                : Response(HttpStatusCode.OK, "{\"data\":[" + RichCardJson + "]}")));
        using var client = new HttpClient(handler);
        var service = new CardInfoService(storage, client, new TestTimeProvider(Now));
        Assert.That((await service.GetCardInfoAsync(1234)).Scale, Is.EqualTo(8));
        using var document = JsonDocument.Parse(storage.RawCache!);
        Assert.That(document.RootElement.GetProperty("SchemaVersion").GetInt32(), Is.EqualTo(2));
        Assert.That(document.RootElement.GetProperty("LastFullRefreshUtc").ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(document.RootElement.GetProperty("Cards").GetProperty("5678").GetProperty("Name").GetString(), Is.EqualTo("Other old name"));
        var nextHandler = HandlerWithBulk(200, (1234, "Current"));
        using var nextClient = new HttpClient(nextHandler);
        var nextService = new CardInfoService(storage, nextClient, new TestTimeProvider(Now));
        Assert.That((await nextService.GetCardInfoAsync(1234)).Name, Is.EqualTo("Current"));
        Assert.That(nextHandler.Requests, Has.Length.EqualTo(1));
    }

    [Test]
    public async Task OptionalMalformedFieldsDoNotDiscardReliableNameOrType() {
        var handler = new RecordingHttpMessageHandler((_, _) => Task.FromResult(Response(HttpStatusCode.OK,
            "{\"data\":[{\"id\":\"bad\",\"name\":\"Skip\"},{\"id\":1234,\"name\":\"Spell\",\"type\":\"Spell Card\",\"race\":\"Quick-Play\",\"level\":\"bad\",\"scale\":{},\"attribute\":42}]}")));
        using var client = new HttpClient(handler);
        var service = new CardInfoService(new TestLocalStorage(null), client, new TestTimeProvider(Now));
        var info = await service.GetCardInfoAsync(1234);
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
        var storage = new TestLocalStorage(null);
        var handler = HandlerWithBulk(200, (1234, "Imported"));
        using var client = new HttpClient(handler);
        var service = new CardInfoService(storage, client, new TestTimeProvider(Now));
        Assert.That(handler.Requests, Is.Empty);
        Assert.That((await service.GetCardInfoAsync(1234)).Name, Is.EqualTo("Imported"));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
    }

    [Test]
    public async Task SingleCardLookupKeepsQueriedPasscodeAndReliableNameFallback() {
        var storage = new TestLocalStorage(CreateCacheJson(Now, (5678, "Other")));
        var handler = new RecordingHttpMessageHandler((_, _) => Task.FromResult(Response(HttpStatusCode.OK,
            "{\"data\":[{\"id\":4321,\"name\":\"Alternate passcode\",\"type\":\"Normal Monster\"}]}")));
        using var client = new HttpClient(handler);
        var service = new CardInfoService(storage, client, new TestTimeProvider(Now));
        var info = await service.GetCardInfoAsync(1234);
        Assert.That(info.Id, Is.EqualTo(1234));
        Assert.That(info.Name, Is.EqualTo("Alternate passcode"));
        Assert.That(await service.GetCardNameAsync(1234), Is.EqualTo(info.Name));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
    }

    [Test]
    public async Task FreshCurrentCache_ReturnsCachedNameWithoutBulkRequest() {
        var storage = new TestLocalStorage(CreateCacheJson(Now.AddDays(-7).AddHours(1), (1234, "Blue-Eyes White Dragon")));
        var handler = new RecordingHttpMessageHandler((_, _) => Task.FromResult(Response(HttpStatusCode.InternalServerError, "{}")));

        using var httpClient = new HttpClient(handler);
        var service = new CardInfoService(storage, httpClient, new TestTimeProvider(Now));

        var result = await service.GetCardNameAsync(1234);

        Assert.That(result, Is.EqualTo("Blue-Eyes White Dragon"));
        Assert.That(handler.Requests, Is.Empty);
    }

    [Test]
    public async Task ExpiredCache_RefreshesOnceAndPersistsCurrentEnvelope() {
        var storage = new TestLocalStorage(CreateCacheJson(Now.AddDays(-7), (1234, "Old Name")));
        var handler = HandlerWithBulk(200, (1234, "Updated Name"), (5678, "Dark Magician"));

        using var httpClient = new HttpClient(handler);
        var service = new CardInfoService(storage, httpClient, new TestTimeProvider(Now));

        var result = await service.GetCardNameAsync(1234);

        Assert.That(result, Is.EqualTo("Updated Name"));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
        AssertPersistedCache(storage.RawCache, Now, (1234, "Updated Name"), (5678, "Dark Magician"));
    }

    [Test]
    public async Task ExpiredCacheAndFailedBulkRefresh_RetainsStaleNameAndTimestamp() {
        var oldTimestamp = Now.AddDays(-10);
        var originalJson = CreateCacheJson(oldTimestamp, (1234, "Stale Name"));
        var storage = new TestLocalStorage(originalJson);
        var handler = HandlerWithStatus(HttpStatusCode.ServiceUnavailable);

        using var httpClient = new HttpClient(handler);
        var service = new CardInfoService(storage, httpClient, new TestTimeProvider(Now));

        var result = await service.GetCardNameAsync(1234);

        Assert.That(result, Is.EqualTo("Stale Name"));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
        Assert.That(storage.RawCache, Is.EqualTo(originalJson));
        Assert.That(storage.WriteCount, Is.Zero);
    }

    [Test]
    public async Task ExpiredCacheAndEmptyBulkResponse_RetainsStaleSnapshot() {
        var originalJson = CreateCacheJson(Now.AddDays(-9), (1234, "Stale Name"));
        var storage = new TestLocalStorage(originalJson);
        var handler = new RecordingHttpMessageHandler((_, _) => Task.FromResult(Response(HttpStatusCode.OK, "{\"data\":[]}")));

        using var httpClient = new HttpClient(handler);
        var service = new CardInfoService(storage, httpClient, new TestTimeProvider(Now));

        Assert.That(await service.GetCardNameAsync(1234), Is.EqualTo("Stale Name"));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
        Assert.That(storage.RawCache, Is.EqualTo(originalJson));
        Assert.That(storage.WriteCount, Is.Zero);
    }

    [Test]
    public async Task LegacyCache_ServesStaleNameAndUpgradesAfterSuccessfulRefresh() {
        var storage = new TestLocalStorage("{\"1234\":\"Legacy Name\"}");
        var handler = HandlerWithBulk(200, (1234, "Refreshed Name"));

        using var httpClient = new HttpClient(handler);
        var service = new CardInfoService(storage, httpClient, new TestTimeProvider(Now));

        var result = await service.GetCardNameAsync(1234);

        Assert.That(result, Is.EqualTo("Refreshed Name"));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
        AssertPersistedCache(storage.RawCache, Now, (1234, "Refreshed Name"));
    }

    [Test]
    public async Task LegacyCacheAndFailedRefresh_RemainsUsableForKnownCard() {
        var storage = new TestLocalStorage("{\"1234\":\"Legacy Name\"}");
        var handler = HandlerWithStatus(HttpStatusCode.ServiceUnavailable);

        using var httpClient = new HttpClient(handler);
        var service = new CardInfoService(storage, httpClient, new TestTimeProvider(Now));

        var result = await service.GetCardNameAsync(1234);

        Assert.That(result, Is.EqualTo("Legacy Name"));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
        Assert.That(storage.WriteCount, Is.Zero);
    }

    [Test]
    public async Task MalformedCache_DoesNotBreakImportAndAttemptsFreshFetch() {
        var storage = new TestLocalStorage("{malformed json");
        var handler = HandlerWithBulk(200, (5678, "Dark Magician"));

        using var httpClient = new HttpClient(handler);
        var service = new CardInfoService(storage, httpClient, new TestTimeProvider(Now));

        var result = await service.GetCardNameAsync(5678);

        Assert.That(result, Is.EqualTo("Dark Magician"));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
        AssertPersistedCache(storage.RawCache, Now, (5678, "Dark Magician"));
    }

    [TestCase(null)]
    [TestCase("{}")]
    public async Task MissingOrEmptyCache_FetchesBulkData(string? rawCache) {
        var storage = new TestLocalStorage(rawCache);
        var handler = HandlerWithBulk(200, (5678, "Dark Magician"));

        using var httpClient = new HttpClient(handler);
        var service = new CardInfoService(storage, httpClient, new TestTimeProvider(Now));

        var result = await service.GetCardNameAsync(5678);

        Assert.That(result, Is.EqualTo("Dark Magician"));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
        AssertPersistedCache(storage.RawCache, Now, (5678, "Dark Magician"));
    }

    [Test]
    public async Task UnsupportedCacheVersion_IsDiscardedAndRefreshed() {
        var storage = new TestLocalStorage("{\"SchemaVersion\":99,\"Cards\":{\"1234\":{\"Name\":\"Future Name\"}}}");
        var handler = HandlerWithBulk(200, (5678, "Dark Magician"));

        using var httpClient = new HttpClient(handler);
        var service = new CardInfoService(storage, httpClient, new TestTimeProvider(Now));

        var result = await service.GetCardNameAsync(5678);

        Assert.That(result, Is.EqualTo("Dark Magician"));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
        AssertPersistedCache(storage.RawCache, Now, (5678, "Dark Magician"));
    }

    [Test]
    public async Task MalformedEnvelopeWithFreshTimestamp_IsDiscardedAndRefreshed() {
        var storage = new TestLocalStorage("{\"SchemaVersion\":1,\"LastFullRefreshUtc\":\"2026-10-01T09:00:00Z\",\"Cards\":{\"not-a-card-id\":{\"Name\":\"Unusable\"}}}");
        var handler = HandlerWithBulk(200, (5678, "Dark Magician"));

        using var httpClient = new HttpClient(handler);
        var service = new CardInfoService(storage, httpClient, new TestTimeProvider(Now));

        Assert.That(await service.GetCardNameAsync(5678), Is.EqualTo("Dark Magician"));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
        AssertPersistedCache(storage.RawCache, Now, (5678, "Dark Magician"));
    }

    [Test]
    public async Task CacheMiss_UsesSingleCardEndpointAndPersistsMetadataEnvelope() {
        var refreshedAt = Now.AddDays(-2);
        var storage = new TestLocalStorage(CreateCacheJson(refreshedAt, (1234, "Known Card")));
        var handler = new RecordingHttpMessageHandler((request, _) => Task.FromResult(
            request.RequestUri!.Query == "?id=5678"
                ? Response(HttpStatusCode.OK, "{\"data\":[{\"id\":5678,\"name\":\"Dark Magician\",\"type\":\"Normal Monster\"}]}")
                : Response(HttpStatusCode.InternalServerError, "{}")));

        using var httpClient = new HttpClient(handler);
        var service = new CardInfoService(storage, httpClient, new TestTimeProvider(Now));

        var result = await service.GetCardNameAsync(5678);

        Assert.That(result, Is.EqualTo("Dark Magician"));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
        AssertPersistedCache(storage.RawCache, refreshedAt, (1234, "Known Card"), (5678, "Dark Magician"));
    }

    [Test]
    public async Task SingleCardFetch_DoesNotAdvanceFullRefreshTimestamp() {
        var refreshedAt = Now.AddDays(-6);
        var storage = new TestLocalStorage(CreateCacheJson(refreshedAt, (1234, "Known Card")));
        var handler = new RecordingHttpMessageHandler((request, _) => Task.FromResult(
            request.RequestUri!.Query == "?id=5678"
                ? Response(HttpStatusCode.OK, "{\"data\":[{\"id\":5678,\"name\":\"New Card\"}]}")
                : Response(HttpStatusCode.InternalServerError, "{}")));

        using var httpClient = new HttpClient(handler);
        var service = new CardInfoService(storage, httpClient, new TestTimeProvider(Now));

        var result = await service.GetCardNameAsync(5678);

        Assert.That(result, Is.EqualTo("New Card"));
        AssertPersistedCache(storage.RawCache, refreshedAt, (1234, "Known Card"), (5678, "New Card"));
    }

    [Test]
    public async Task SingleCardFetchAfterLegacyBulkFailure_PersistsUnknownTimestampAndNextInstanceRetriesBulk() {
        var storage = new TestLocalStorage("{\"1234\":\"Legacy Name\"}");
        var firstHandler = new RecordingHttpMessageHandler((request, _) => Task.FromResult(
            request.RequestUri!.Query.Length == 0
                ? Response(HttpStatusCode.ServiceUnavailable, "{}")
                : Response(HttpStatusCode.OK, "{\"data\":[{\"id\":5678,\"name\":\"New Card\"}]}") ));

        using (var firstHttpClient = new HttpClient(firstHandler)) {
            var firstService = new CardInfoService(storage, firstHttpClient, new TestTimeProvider(Now));
            Assert.That(await firstService.GetCardNameAsync(5678), Is.EqualTo("New Card"));
        }

        using (var document = JsonDocument.Parse(storage.RawCache!)) {
            var root = document.RootElement;
            Assert.That(root.GetProperty("SchemaVersion").GetInt32(), Is.EqualTo(2));
            Assert.That(root.GetProperty("LastFullRefreshUtc").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(root.GetProperty("Cards").GetProperty("1234").GetProperty("Name").GetString(), Is.EqualTo("Legacy Name"));
            Assert.That(root.GetProperty("Cards").GetProperty("5678").GetProperty("Name").GetString(), Is.EqualTo("New Card"));
        }

        var nextHandler = HandlerWithBulk(200, (1234, "Refreshed Name"));
        using var nextHttpClient = new HttpClient(nextHandler);
        var nextService = new CardInfoService(storage, nextHttpClient, new TestTimeProvider(Now));

        Assert.That(await nextService.GetCardNameAsync(1234), Is.EqualTo("Refreshed Name"));
        Assert.That(nextHandler.Requests, Has.Length.EqualTo(1));
    }

    [Test]
    public async Task BulkAndSingleHttpFailures_FallBackToNumericId() {
        var storage = new TestLocalStorage(null);
        var handler = HandlerWithStatus(HttpStatusCode.ServiceUnavailable);

        using var httpClient = new HttpClient(handler);
        var service = new CardInfoService(storage, httpClient, new TestTimeProvider(Now));

        var result = await service.GetCardNameAsync(9999);

        Assert.That(result, Is.EqualTo("9999"));
        Assert.That(handler.Requests, Has.Length.EqualTo(2));
    }

    [Test]
    public async Task LocalStorageReadAndWriteFailures_DoNotBreakImport() {
        var storage = new TestLocalStorage(null) { ThrowOnRead = true, ThrowOnWrite = true };
        var handler = HandlerWithBulk(200, (5678, "Dark Magician"));

        using var httpClient = new HttpClient(handler);
        var service = new CardInfoService(storage, httpClient, new TestTimeProvider(Now));

        var result = await service.GetCardNameAsync(5678);

        Assert.That(result, Is.EqualTo("Dark Magician"));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
        Assert.That(storage.WriteCount, Is.Zero);
    }

    [Test]
    public async Task ParallelStartupLookups_ShareOneBulkRefresh() {
        var storage = new TestLocalStorage(CreateCacheJson(Now.AddDays(-8), (1234, "Old A"), (5678, "Old B")));
        var requestStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseResponse = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new RecordingHttpMessageHandler((_, _) => {
            requestStarted.TrySetResult(true);
            return releaseResponse.Task;
        });

        using var httpClient = new HttpClient(handler);
        var service = new CardInfoService(storage, httpClient, new TestTimeProvider(Now));

        var firstLookup = service.GetCardNameAsync(1234);
        var secondLookup = service.GetCardNameAsync(5678);
        await requestStarted.Task;
        Assert.That(handler.Requests, Has.Length.EqualTo(1));

        releaseResponse.SetResult(Response(HttpStatusCode.OK, CreateBulkJson((1234, "New A"), (5678, "New B"))));
        var results = await Task.WhenAll(firstLookup, secondLookup);

        Assert.That(results, Is.EqualTo(new[] { "New A", "New B" }));
        Assert.That(handler.Requests, Has.Length.EqualTo(1));
    }

    private static RecordingHttpMessageHandler HandlerWithBulk(int statusCode, params (int Id, string Name)[] cards) =>
        new((request, _) => Task.FromResult(request.RequestUri!.Query.Length == 0
            ? Response((HttpStatusCode)statusCode, CreateBulkJson(cards))
            : Response(HttpStatusCode.InternalServerError, "{}")));

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

    private static string CreateCacheJson(DateTimeOffset refreshedAt, params (int Id, string Name)[] cards) =>
        JsonSerializer.Serialize(new {
            SchemaVersion = 2,
            LastFullRefreshUtc = refreshedAt,
            Cards = cards.ToDictionary(card => card.Id.ToString(), card => new { Id = card.Id, Name = card.Name, Type = "Effect Monster" })
        });

    private static void AssertPersistedCache(string? json, DateTimeOffset expectedTimestamp, params (int Id, string Name)[] expectedCards) {
        Assert.That(json, Is.Not.Null);
        using var document = JsonDocument.Parse(json!);
        var root = document.RootElement;
        Assert.That(root.GetProperty("SchemaVersion").GetInt32(), Is.EqualTo(2));
        Assert.That(root.GetProperty("LastFullRefreshUtc").GetDateTimeOffset(), Is.EqualTo(expectedTimestamp));
        var cards = root.GetProperty("Cards");
        Assert.That(cards.EnumerateObject().Count(), Is.EqualTo(expectedCards.Length));
        foreach (var (id, name) in expectedCards)
            Assert.That(cards.GetProperty(id.ToString()).GetProperty("Name").GetString(), Is.EqualTo(name));
    }

    private sealed class TestLocalStorage(string? rawCache) : ILocalStorageService {
        public string? RawCache { get; private set; } = rawCache;
        public bool ThrowOnRead { get; init; }
        public bool ThrowOnWrite { get; init; }
        public int WriteCount { get; private set; }

        public Task<T?> GetItemAsync<T>(string key) =>
            Task.FromResult(RawCache is null ? default : JsonSerializer.Deserialize<T>(RawCache));

        public Task<string?> GetRawItemAsync(string key) {
            if (ThrowOnRead)
                throw new InvalidOperationException("Storage read failed.");
            return Task.FromResult(RawCache);
        }

        public Task SetItemAsync<T>(string key, T value) {
            if (ThrowOnWrite)
                throw new InvalidOperationException("Storage write failed.");
            RawCache = JsonSerializer.Serialize(value);
            WriteCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class TestTimeProvider(DateTimeOffset now) : TimeProvider {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory) : HttpMessageHandler {
        private readonly ConcurrentQueue<string> _requests = new();

        public string[] Requests => _requests.ToArray();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            _requests.Enqueue(request.RequestUri!.ToString());
            return responseFactory(request, cancellationToken);
        }
    }
}
