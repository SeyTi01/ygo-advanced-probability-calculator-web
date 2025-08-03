using System.Net;
using Moq;
using Moq.Protected;
using YGOProbabilityCalculatorBlazor.Services.DeckImport;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazorTest.Services.DeckImport;

[TestFixture]
public class CardInfoServiceTests {
    private Mock<ILocalStorageService> _localStorageMock;
    private Mock<HttpMessageHandler> _httpMessageHandlerMock;
    private CardInfoService _cardInfoService;
    private Dictionary<int, string> _testCache;
    private HttpClient _httpClient;

    [SetUp]
    public void Setup() {
        _localStorageMock = new Mock<ILocalStorageService>();
        _httpMessageHandlerMock = new Mock<HttpMessageHandler>();
        _testCache = new Dictionary<int, string> {
            { 1234, "Blue-Eyes White Dragon" }
        };

        _localStorageMock.Setup(x => x.GetItemAsync<Dictionary<int, string>>("cardCache"))
            .ReturnsAsync(_testCache);

        _httpClient = new HttpClient(_httpMessageHandlerMock.Object);
        _cardInfoService = new CardInfoService(_localStorageMock.Object, _httpClient);
    }

    [TearDown]
    public void TearDown() {
        _httpClient.Dispose();
    }

    [Test]
    public async Task GetCardNameAsync_CachedCard_ReturnsCachedName() {
        const int cardId = 1234;
        const string expectedName = "Blue-Eyes White Dragon";

        var result = await _cardInfoService.GetCardNameAsync(cardId);

        Assert.That(result, Is.EqualTo(expectedName));
    }

    [Test]
    public async Task GetCardNameAsync_NonExistentCard_ReturnsIdAsString() {
        const int cardId = 9999;

        SetupHttpMockResponse("{\"data\": []}");

        var result = await _cardInfoService.GetCardNameAsync(cardId);

        Assert.That(result, Is.EqualTo(cardId.ToString()));
    }

    [Test]
    public void Constructor_EmptyCache_InitiatesFetchAllCards() {
        _localStorageMock.Reset();

        _localStorageMock.Setup(x => x.GetItemAsync<Dictionary<int, string>>("cardCache"))
            .ReturnsAsync(new Dictionary<int, string>());

        SetupHttpMockResponse("{\"data\": [{\"id\": 5678, \"name\": \"Dark Magician\"}]}");

        var service = new CardInfoService(_localStorageMock.Object, _httpClient);

        _localStorageMock.Verify(x => x.GetItemAsync<Dictionary<int, string>>("cardCache"), Times.Exactly(1));
    }

    [Test]
    public void Constructor_LoadCacheFailure_CreatesEmptyCache() {
        _localStorageMock.Setup(x => x.GetItemAsync<Dictionary<int, string>>("cardCache"))
            .ThrowsAsync(new Exception());

        var service = new CardInfoService(_localStorageMock.Object, _httpClient);

        Assert.DoesNotThrowAsync(async () => await service.GetCardNameAsync(1234));
    }

    [Test]
    public async Task SaveCache_OnNewCard_CallsSetItemAsync() {
        const int cardId = 5678;
        const string cardName = "Dark Magician";

        _localStorageMock.Setup(x => x.GetItemAsync<Dictionary<int, string>>("cardCache"))
            .ReturnsAsync(new Dictionary<int, string>());

        SetupHttpMockResponse($"{{\"data\": [{{\"id\": {cardId}, \"name\": \"{cardName}\"}}]}}");

        var service = new CardInfoService(_localStorageMock.Object, _httpClient);
        await service.GetCardNameAsync(cardId);

        _localStorageMock.Verify(x => x.SetItemAsync(
            "cardCache",
            It.Is<Dictionary<int, string>>(d => d.ContainsKey(cardId) && d[cardId] == cardName)
        ), Times.Once);
    }

    private void SetupHttpMockResponse(string content) {
        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(content)
            });
    }
}
