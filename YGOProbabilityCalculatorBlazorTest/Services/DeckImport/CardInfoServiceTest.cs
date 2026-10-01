using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Services.DeckImport;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazorTest.Services.DeckImport;

[TestFixture]
public class CardInfoServiceTests {
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

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
            Assert.That(root.GetProperty("SchemaVersion").GetInt32(), Is.EqualTo(1));
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
            SchemaVersion = 1,
            LastFullRefreshUtc = refreshedAt,
            Cards = cards.ToDictionary(card => card.Id.ToString(), card => new { Name = card.Name })
        });

    private static void AssertPersistedCache(string? json, DateTimeOffset expectedTimestamp, params (int Id, string Name)[] expectedCards) {
        Assert.That(json, Is.Not.Null);
        using var document = JsonDocument.Parse(json!);
        var root = document.RootElement;
        Assert.That(root.GetProperty("SchemaVersion").GetInt32(), Is.EqualTo(1));
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
