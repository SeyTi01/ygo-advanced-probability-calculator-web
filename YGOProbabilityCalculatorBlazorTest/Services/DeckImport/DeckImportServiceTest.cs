using Microsoft.AspNetCore.Components.Forms;
using Moq;
using YGOProbabilityCalculatorBlazor.Services.DeckImport;
using YGOProbabilityCalculatorBlazor.Models;
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

        _cardInfoServiceMock.Setup(x => x.GetCardInfoAsync(12345))
            .ReturnsAsync(new CardInfo { Id = 12345, Name = "Test Card 1" });
        _cardInfoServiceMock.Setup(x => x.GetCardInfoAsync(67890))
            .ReturnsAsync(new CardInfo { Id = 67890, Name = "Test Card 2" });

        var result = await _service.ImportDeckFromYdkAsync(mockFile.Object);

        Assert.That(result, Has.Count.EqualTo(2));

        var firstCard = result.First(x => x.Copies == 2);
        Assert.Multiple(() => {
            Assert.That(firstCard.ExternalCardId, Is.EqualTo(12345));
            Assert.That(firstCard.Name, Is.EqualTo("Test Card 1"));
            Assert.That(firstCard.Copies, Is.EqualTo(2));
            Assert.That(firstCard.Categories, Is.Empty);
            Assert.That(firstCard.Active, Is.True);
        });

        var secondCard = result.First(x => x.Copies == 1);
        Assert.Multiple(() => {
            Assert.That(secondCard.ExternalCardId, Is.EqualTo(67890));
            Assert.That(secondCard.Copies, Is.EqualTo(1));
            Assert.That(secondCard.Categories, Is.Empty);
            Assert.That(secondCard.Active, Is.True);
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

        _cardInfoServiceMock.Setup(x => x.GetCardInfoAsync(12345))
            .ThrowsAsync(new Exception("API failure"));

        var result = await _service.ImportDeckFromYdkAsync(mockFile.Object);

        Assert.That(result, Has.Count.EqualTo(1));
        var card = result[0];
        Assert.Multiple(() => {
            Assert.That(card.ExternalCardId, Is.EqualTo(12345));
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

        _cardInfoServiceMock.Setup(x => x.GetCardInfoAsync(12345))
            .ReturnsAsync(new CardInfo { Id = 12345, Name = "Test Card" });

        var result = await _service.ImportDeckFromYdkAsync(mockFile.Object);

        Assert.That(result, Has.Count.EqualTo(1));
        var card = result[0];
        Assert.Multiple(() => {
            Assert.That(card.ExternalCardId, Is.EqualTo(12345));
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

    [Test]
    public async Task ImportAttachesFlatPropertiesKeepsOrderAndDoesNotCreateUserCategories() {
        _fileServiceMock.Setup(x => x.ReadAllLinesAsync(It.IsAny<IBrowserFile>()))
            .ReturnsAsync(["#main", "123", "456", "123", "#extra", "789"]);
        _cardInfoServiceMock.Setup(x => x.GetCardInfoAsync(123)).ReturnsAsync(new CardInfo {
            Id = 123, Name = "Quick spell", Type = "Spell Card", Race = "Quick-Play"
        });
        _cardInfoServiceMock.Setup(x => x.GetCardInfoAsync(456)).ThrowsAsync(new Exception("Unavailable"));
        var cards = await _service.ImportDeckFromYdkAsync(Mock.Of<IBrowserFile>());
        Assert.That(cards.Select(c => c.ExternalCardId), Is.EqualTo(new[] { 123, 456 }));
        Assert.That(cards.Select(c => c.Copies), Is.EqualTo(new[] { 2, 1 }));
        Assert.That(cards[0].Categories.Select(c => c.Name), Is.EqualTo(new[] { "Spell", "Quick-Play Spell" }));
        Assert.That(cards[0].Categories.All(c => c.Source == CategorySource.Metadata), Is.True);
        Assert.That(cards.All(c => c.ManualMetadataCategoryKeys.Count == 0), Is.True);
        Assert.That(cards[1].Categories, Is.Empty);
        Assert.That(cards[1].Name, Is.EqualTo("456"));
        _cardInfoServiceMock.Verify(x => x.GetCardInfoAsync(123), Times.Once);
        _cardInfoServiceMock.Verify(x => x.GetCardInfoAsync(789), Times.Never);
    }
}
