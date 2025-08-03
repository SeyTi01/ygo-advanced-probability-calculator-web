using Microsoft.AspNetCore.Components.Forms;
using Moq;
using YGOProbabilityCalculatorBlazor.Services.DeckImport;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazorTest.Services.DeckImport;

public class DeckImportServiceTest {
    private DeckImportService _service = null!;
    private Mock<ICardInfoService> _cardInfoServiceMock = null!;
    private Mock<IFileService> _fileServiceMock = null!;

    [SetUp]
    public void Setup() {
        _cardInfoServiceMock = new Mock<ICardInfoService>();
        _fileServiceMock = new Mock<IFileService>();
        _service = new DeckImportService(_cardInfoServiceMock.Object, _fileServiceMock.Object);
    }

    [Test]
    public async Task ImportDeckFromYdkAsync_ValidFile_ReturnsCorrectCards() {
        var mockFile = new Mock<IBrowserFile>();
        _fileServiceMock.Setup(x => x.ReadAllLinesAsync(It.IsAny<IBrowserFile>()))
            .ReturnsAsync([
                "#main",
                "12345",
                "12345",
                "67890",
                "#extra",
                "11111"
            ]);

        _cardInfoServiceMock.Setup(x => x.GetCardNameAsync(12345))
            .ReturnsAsync("Test Card 1");
        _cardInfoServiceMock.Setup(x => x.GetCardNameAsync(67890))
            .ReturnsAsync("Test Card 2");

        var result = await _service.ImportDeckFromYdkAsync(mockFile.Object);

        Assert.That(result, Has.Count.EqualTo(2));

        var firstCard = result.First(x => x.Copies == 2);
        Assert.Multiple(() => {
            Assert.That(firstCard.Copies, Is.EqualTo(2));
            Assert.That(firstCard.Categories, Is.Empty);
        });

        var secondCard = result.First(x => x.Copies == 1);
        Assert.Multiple(() => {
            Assert.That(secondCard.Copies, Is.EqualTo(1));
            Assert.That(secondCard.Categories, Is.Empty);
        });
    }

    [Test]
    public async Task ImportDeckFromYdkAsync_CardInfoServiceFails_CreatesCardAnyway() {
        var mockFile = new Mock<IBrowserFile>();
        _fileServiceMock.Setup(x => x.ReadAllLinesAsync(It.IsAny<IBrowserFile>()))
            .ReturnsAsync([
                "#main",
                "12345"
            ]);

        _cardInfoServiceMock.Setup(x => x.GetCardNameAsync(12345))
            .ThrowsAsync(new Exception("API failure"));

        var result = await _service.ImportDeckFromYdkAsync(mockFile.Object);

        Assert.That(result, Has.Count.EqualTo(1));
        var card = result[0];
        Assert.Multiple(() => {
            Assert.That(card.Copies, Is.EqualTo(1));
            Assert.That(card.Categories, Is.Empty);
        });
    }

    [Test]
    public async Task ImportDeckFromYdkAsync_InvalidCardId_SkipsInvalidLines() {
        var mockFile = new Mock<IBrowserFile>();
        _fileServiceMock.Setup(x => x.ReadAllLinesAsync(It.IsAny<IBrowserFile>()))
            .ReturnsAsync([
                "#main",
                "invalid",
                "12345",
                "not a number"
            ]);

        _cardInfoServiceMock.Setup(x => x.GetCardNameAsync(12345))
            .ReturnsAsync("Test Card");

        var result = await _service.ImportDeckFromYdkAsync(mockFile.Object);

        Assert.That(result, Has.Count.EqualTo(1));
        var card = result[0];
        Assert.Multiple(() => {
            Assert.That(card.Copies, Is.EqualTo(1));
            Assert.That(card.Categories, Is.Empty);
        });
    }

    [Test]
    public void ImportDeckFromYdkAsync_FileServiceThrows_ThrowsException() {
        var mockFile = new Mock<IBrowserFile>();
        _fileServiceMock.Setup(x => x.ReadAllLinesAsync(It.IsAny<IBrowserFile>()))
            .ThrowsAsync(new Exception("File read error"));

        Assert.ThrowsAsync<Exception>(async () =>
            await _service.ImportDeckFromYdkAsync(mockFile.Object));
    }
}
