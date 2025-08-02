using Moq;
using YGOProbabilityCalculatorBlazor.Services.DeckImport;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazorTest.Services.DeckImport;

[TestFixture]
public class CardInfoServiceTests {
    private Mock<IFileService> _fileServiceMock;
    private Mock<ISerializer> _serializerMock;
    private CardInfoService _cardInfoService;
    private Dictionary<int, string> _testCache;

    [SetUp]
    public void Setup() {
        _fileServiceMock = new Mock<IFileService>();
        _serializerMock = new Mock<ISerializer>();
        _testCache = new Dictionary<int, string> {
            { 1234, "Blue-Eyes White Dragon" }
        };

        _fileServiceMock.Setup(x => x.ReadAllTextAsync(It.IsAny<string>()))
            .ReturnsAsync("{}");

        _serializerMock.Setup(x => x.Deserialize<Dictionary<int, string>>(It.IsAny<string>()))
            .Returns(_testCache);

        _serializerMock.Setup(x => x.Serialize(It.IsAny<Dictionary<int, string>>()))
            .Returns("{}");

        _cardInfoService = new CardInfoService(_fileServiceMock.Object, _serializerMock.Object);
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

        var result = await _cardInfoService.GetCardNameAsync(cardId);

        Assert.That(result, Is.EqualTo(cardId.ToString()));
    }

    [Test]
    public void Constructor_EmptyCache_InitiatesFetchAllCards() {
        var emptyCache = new Dictionary<int, string>();
        _serializerMock.Setup(x => x.Deserialize<Dictionary<int, string>>(It.IsAny<string>()))
            .Returns(emptyCache);

        var service = new CardInfoService(_fileServiceMock.Object, _serializerMock.Object);

        _fileServiceMock.Verify(x => x.ReadAllTextAsync(It.IsAny<string>()), Times.AtLeastOnce);
        _serializerMock.Verify(x => x.Deserialize<Dictionary<int, string>>(It.IsAny<string>()), Times.AtLeastOnce);
    }

    [Test]
    public void Constructor_LoadCacheFailure_CreatesEmptyCache() {
        _fileServiceMock.Setup(x => x.ReadAllTextAsync(It.IsAny<string>()))
            .ThrowsAsync(new IOException());

        var service = new CardInfoService(_fileServiceMock.Object, _serializerMock.Object);

        Assert.DoesNotThrowAsync(async () => await service.GetCardNameAsync(1234));
    }
}
