using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Moq;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.DeckImport;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazorTest.Services.DeckImport;

[TestFixture]
public class CardArtworkLoadingTest
{
    private const string Json =
        """{"data":[{"id":1,"name":"Card","type":"Effect Monster","card_images":[{"id":1,"image_url_small":"https://images.ygoprodeck.com/images/cards_small/1.jpg"}]}]}""";

    private static Mock<ILocalStorageService> Storage(string? cache = null)
    {
        Mock<ILocalStorageService> storage = new();
        storage.Setup(x => x.GetRawItemAsync("cardCache")).ReturnsAsync(cache);

        return storage;
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(request.RequestUri!.ToString());

            return Task.FromResult(response(request));
        }
    }

    private static HttpResponseMessage Response(HttpStatusCode status, string json = "{}") =>
        new(status) { Content = new StringContent(json) };

    [Test]
    public async Task ArtworkFromEmptyCacheUsesOnlyRequestedPasscodeAndDoesNotPreloadCatalog()
    {
        using Handler handler = new(_ => Response(HttpStatusCode.OK, Json));
        using HttpClient http = new(handler);
        CardInfoService info = new(Storage().Object, http);
        CardArtworkService service = new(info);
        Assert.That(await service.GetArtworkUrlAsync(1), Does.EndWith("/small/1.jpg"));
        Assert.That(await service.GetArtworkUrlAsync(1), Does.EndWith("/small/1.jpg"));
        Assert.That(handler.Requests, Is.EqualTo(new[] { "https://db.ygoprodeck.com/api/v7/cardinfo.php?id=1" }));
    }

    [Test]
    public async Task ExpiredValidatedArtworkMetadataReusesRetainedIdentityWithoutCatalogRefresh()
    {
        string cache = JsonSerializer.Serialize(new
            {
                SchemaVersion = 2, LastFullRefreshUtc = DateTimeOffset.UtcNow.AddYears(-1),
                Cards = new Dictionary<string, object>
                {
                    ["2"] = new
                    {
                        Id = 2, Name = "Alternate", CanonicalCardId = 1,
                        ArtworkMetadataKnown = true, ArtworkImageIds = new[] { 1, 2 }
                    }
                }
            }
        );
        using Handler handler = new(_ => throw new AssertionException("No metadata request expected"));
        using HttpClient http = new(handler);
        CardArtworkService service = new(new CardInfoService(Storage(cache).Object, http));
        Assert.That(await service.GetArtworkUrlAsync(2), Does.EndWith("/small/2.jpg"));
        Assert.That(handler.Requests, Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task TransientMetadataFailureIsRetryableAndExposesSecondsOrDateRetryAfter(bool date)
    {
        int calls = 0;
        using Handler handler = new(_ =>
            {
                if (++calls > 1)
                {
                    return Response(HttpStatusCode.OK, Json);
                }

                HttpResponseMessage response = Response(HttpStatusCode.TooManyRequests);
                response.Headers.RetryAfter = date
                    ? new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddMinutes(3))
                    : new RetryConditionHeaderValue(TimeSpan.FromMinutes(3));

                return response;
            }
        );
        using HttpClient http = new(handler);
        CardArtworkService service = new(new CardInfoService(Storage().Object, http));
        CardArtworkLookupException? error =
            Assert.ThrowsAsync<CardArtworkLookupException>(async () => await service.GetArtworkUrlAsync(1));
        Assert.That(error!.RetryAfter.TotalSeconds, Is.InRange(178, 181));
        Assert.That(await service.GetArtworkUrlAsync(1), Does.EndWith("/small/1.jpg"));
        Assert.That(handler.Requests, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task SharedFailedMetadataTaskIsEvictedOnceAndNextConsumersShareRecovery()
    {
        TaskCompletionSource<CardInfo> pending =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        Mock<ICardInfoService> metadata = new();
        metadata.Setup(x => x.GetCardArtworkInfoAsync(1)).Returns(pending.Task);
        CardArtworkService service = new(metadata.Object);
        Task<string?> first = service.GetArtworkUrlAsync(1);
        Task<string?> second = service.GetArtworkUrlAsync(1);
        pending.SetException(new HttpRequestException());
        Assert.ThrowsAsync<HttpRequestException>(async () => await first);
        Assert.ThrowsAsync<HttpRequestException>(async () => await second);
        metadata
            .Setup(x => x.GetCardArtworkInfoAsync(1))
            .ReturnsAsync(new CardInfo
                { Id = 1, ArtworkMetadataKnown = true, ArtworkImageIds = new[] { 1 } }
            );
        Assert.That(await service.GetArtworkUrlAsync(1), Does.EndWith("/small/1.jpg"));
        Assert.That(await service.GetArtworkUrlAsync(1), Does.EndWith("/small/1.jpg"));
        metadata.Verify(x => x.GetCardArtworkInfoAsync(1), Times.Exactly(2));
    }

    [Test]
    public async Task AuthoritativeNotFoundIsCachedWithoutWritingMetadata()
    {
        Mock<ILocalStorageService> storage = Storage();
        using Handler handler = new(_ => Response(HttpStatusCode.NotFound));
        using HttpClient http = new(handler);
        CardArtworkService service = new(new CardInfoService(storage.Object, http));
        Assert.That(await service.GetArtworkUrlAsync(1), Is.Null);
        Assert.That(await service.GetArtworkUrlAsync(1), Is.Null);
        Assert.That(handler.Requests, Has.Count.EqualTo(1));
        storage.Verify(x => x.SetItemAsync(It.IsAny<string>(), It.IsAny<object>()), Times.Never);
    }

    private sealed class GateHandler : HttpMessageHandler
    {
        public TaskCompletionSource<HttpResponseMessage> Response { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Started.TrySetResult();

            return Response.Task;
        }
    }

    [Test]
    public async Task SlowArtworkMetadataDoesNotHoldImportOrSessionEnrichmentGate()
    {
        string cache = JsonSerializer.Serialize(new
            {
                SchemaVersion = 2, LastFullRefreshUtc = DateTimeOffset.UtcNow,
                Cards = new Dictionary<string, object>
                {
                    ["2"] = new
                    {
                        Id = 2, Name = "Other", Type = "Spell Card", Race = "Normal",
                        ArtworkMetadataKnown = true, ArtworkImageIds = new[] { 2 }
                    }
                }
            }
        );
        using GateHandler handler = new();
        using HttpClient http = new(handler);
        CardInfoService service = new(Storage(cache).Object, http);
        Task<CardInfo> pending = service.GetCardArtworkInfoAsync(1);
        await handler.Started.Task;
        Assert.That(await service.GetCardNameAsync(2).WaitAsync(TimeSpan.FromSeconds(1)), Is.EqualTo("Other"));
        Assert.That((await service.GetCardArtworkInfoAsync(2).WaitAsync(TimeSpan.FromSeconds(1))).ArtworkImageIds,
            Is.EqualTo(new[] { 2 })
        );
        IReadOnlyDictionary<string, CardInfo> names = await service
            .GetCardInfoByExactNamesAsync(new[] { "Other" })
            .WaitAsync(TimeSpan.FromSeconds(1));
        Assert.That(names.Keys, Is.EqualTo(new[] { "Other" }));
        Assert.That(pending.IsCompleted, Is.False);
        handler.Response.SetResult(Response(HttpStatusCode.OK, Json));
        await pending;
    }
}
