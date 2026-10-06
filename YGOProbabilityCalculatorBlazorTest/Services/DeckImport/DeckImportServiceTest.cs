using System.Buffers.Binary;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Forms;
using Moq;
using YGOProbabilityCalculatorBlazor.Services.DeckImport;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.Converter;

namespace YGOProbabilityCalculatorBlazorTest.Services.DeckImport;

public class DeckImportServiceTest
{
    private DeckImportService _service = null!;
    private Mock<ICardInfoService> _cardInfoServiceMock = null!;
    private Mock<IFileService> _fileServiceMock = null!;

    [SetUp]
    public void Setup()
    {
        _cardInfoServiceMock = new Mock<ICardInfoService>();
        _fileServiceMock = new Mock<IFileService>();
        _service = new DeckImportService(_cardInfoServiceMock.Object, _fileServiceMock.Object);
    }

    [Test]
    public async Task ImportDeckFromYdkAsync_ValidFile_ReturnsCorrectCards()
    {
        Mock<IBrowserFile> mockFile = new();
        _fileServiceMock
            .Setup(x => x.ReadAllLinesAsync(It.IsAny<IBrowserFile>()))
            .ReturnsAsync([
                    "#main",
                    "12345",
                    "12345",
                    "67890",
                    "#extra",
                    "11111"
                ]
            );

        _cardInfoServiceMock
            .Setup(x => x.GetCardInfoAsync(12345))
            .ReturnsAsync(new CardInfo { Id = 12345, Name = "Test Card 1" });
        _cardInfoServiceMock
            .Setup(x => x.GetCardInfoAsync(67890))
            .ReturnsAsync(new CardInfo { Id = 67890, Name = "Test Card 2" });

        List<Card> result = await _service.ImportDeckFromYdkAsync(mockFile.Object);

        Assert.That(result, Has.Count.EqualTo(2));

        Card firstCard = result.First(x => x.Copies == 2);
        Assert.Multiple(() =>
            {
                Assert.That(firstCard.ExternalCardId, Is.EqualTo(12345));
                Assert.That(firstCard.Name, Is.EqualTo("Test Card 1"));
                Assert.That(firstCard.Copies, Is.EqualTo(2));
                Assert.That(firstCard.Categories, Is.Empty);
                Assert.That(firstCard.Active, Is.True);
            }
        );

        Card secondCard = result.First(x => x.Copies == 1);
        Assert.Multiple(() =>
            {
                Assert.That(secondCard.ExternalCardId, Is.EqualTo(67890));
                Assert.That(secondCard.Copies, Is.EqualTo(1));
                Assert.That(secondCard.Categories, Is.Empty);
                Assert.That(secondCard.Active, Is.True);
            }
        );
    }

    [Test]
    public async Task ImportDeckFromYdkAsync_CardInfoServiceFails_CreatesCardAnyway()
    {
        Mock<IBrowserFile> mockFile = new();
        _fileServiceMock
            .Setup(x => x.ReadAllLinesAsync(It.IsAny<IBrowserFile>()))
            .ReturnsAsync([
                    "#main",
                    "12345"
                ]
            );

        _cardInfoServiceMock
            .Setup(x => x.GetCardInfoAsync(12345))
            .ThrowsAsync(new Exception("API failure"));

        List<Card> result = await _service.ImportDeckFromYdkAsync(mockFile.Object);

        Assert.That(result, Has.Count.EqualTo(1));
        Card card = result[0];
        Assert.Multiple(() =>
            {
                Assert.That(card.ExternalCardId, Is.EqualTo(12345));
                Assert.That(card.Copies, Is.EqualTo(1));
                Assert.That(card.Categories, Is.Empty);
            }
        );
    }

    [Test]
    public async Task ImportDeckFromYdkAsync_InvalidCardId_SkipsInvalidLines()
    {
        Mock<IBrowserFile> mockFile = new();
        _fileServiceMock
            .Setup(x => x.ReadAllLinesAsync(It.IsAny<IBrowserFile>()))
            .ReturnsAsync([
                    "#main",
                    "invalid",
                    "12345",
                    "not a number"
                ]
            );

        _cardInfoServiceMock
            .Setup(x => x.GetCardInfoAsync(12345))
            .ReturnsAsync(new CardInfo { Id = 12345, Name = "Test Card" });

        List<Card> result = await _service.ImportDeckFromYdkAsync(mockFile.Object);

        Assert.That(result, Has.Count.EqualTo(1));
        Card card = result[0];
        Assert.Multiple(() =>
            {
                Assert.That(card.ExternalCardId, Is.EqualTo(12345));
                Assert.That(card.Copies, Is.EqualTo(1));
                Assert.That(card.Categories, Is.Empty);
            }
        );
    }

    [Test]
    public void ImportDeckFromYdkAsync_FileServiceThrows_ThrowsException()
    {
        Mock<IBrowserFile> mockFile = new();
        _fileServiceMock
            .Setup(x => x.ReadAllLinesAsync(It.IsAny<IBrowserFile>()))
            .ThrowsAsync(new Exception("File read error"));

        Assert.ThrowsAsync<Exception>(async () =>
            await _service.ImportDeckFromYdkAsync(mockFile.Object)
        );
    }

    [Test]
    public async Task ImportAttachesFlatPropertiesKeepsOrderAndDoesNotCreateUserCategories()
    {
        _fileServiceMock
            .Setup(x => x.ReadAllLinesAsync(It.IsAny<IBrowserFile>()))
            .ReturnsAsync(["#main", "123", "456", "123", "#extra", "789"]);
        _cardInfoServiceMock
            .Setup(x => x.GetCardInfoAsync(123))
            .ReturnsAsync(new CardInfo
                {
                    Id = 123, Name = "Quick spell", Type = "Spell Card", Race = "Quick-Play"
                }
            );
        _cardInfoServiceMock.Setup(x => x.GetCardInfoAsync(456)).ThrowsAsync(new Exception("Unavailable"));
        List<Card> cards = await _service.ImportDeckFromYdkAsync(Mock.Of<IBrowserFile>());
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

    [Test]
    public async Task ImportDeckFromYdkeAsync_CanonicalFixtureImportsOnlyMainCardsWithMetadata()
    {
        const string code = "ydke://o6lXBZyFNAI=!viOnAg==!7ydRAA==!";
        CardInfo firstInfo = new()
        {
            Id = 89631139, Name = "First main card", Type = "Effect Monster",
            Race = "Warrior", Attribute = "DARK", Level = 4
        };
        CardInfo secondInfo = new()
        {
            Id = 36996508, Name = "Second main card", Type = "Spell Card", Race = "Quick-Play"
        };
        _cardInfoServiceMock.Setup(service => service.GetCardInfoAsync(89631139)).ReturnsAsync(firstInfo);
        _cardInfoServiceMock.Setup(service => service.GetCardInfoAsync(36996508)).ReturnsAsync(secondInfo);

        List<Card> cards = await _service.ImportDeckFromYdkeAsync(code);

        Assert.That(cards.Select(card => card.ExternalCardId), Is.EqualTo(new int?[] { 89631139, 36996508 }));
        Assert.That(cards.Select(card => card.Copies), Is.EqualTo(new[] { 1, 1 }));
        Assert.That(cards.Select(card => card.Name), Is.EqualTo(new[] { "First main card", "Second main card" }));
        Assert.That(cards.Select(card => card.Categories.Select(category => category.Identity).ToArray()),
            Is.EqualTo(new[]
                {
                    CardPropertyProvider.GetCategories(firstInfo).Select(category => category.Identity).ToArray(),
                    [.. CardPropertyProvider.GetCategories(secondInfo).Select(category => category.Identity)]
                }
            )
        );
        Assert.That(cards.All(card => card.Categories.All(category => category.Source == CategorySource.Metadata)),
            Is.True
        );
        Assert.That(cards.All(card => card.ManualMetadataCategoryKeys.Count == 0), Is.True);
        _cardInfoServiceMock.Verify(service => service.GetCardInfoAsync(89631139), Times.Once);
        _cardInfoServiceMock.Verify(service => service.GetCardInfoAsync(36996508), Times.Once);
        _cardInfoServiceMock.Verify(service => service.GetCardInfoAsync(44508094), Times.Never);
        _cardInfoServiceMock.Verify(service => service.GetCardInfoAsync(5318639), Times.Never);
    }

    [Test]
    public async Task ImportDeckFromYdkeAsync_CollapsesDuplicatesInFirstOccurrenceOrder()
    {
        string code = BuildYdke([123, 456, 123, 789, 456], [999], [888]);

        foreach (int id in new[] { 123, 456, 789 })
        {
            _cardInfoServiceMock
                .Setup(service => service.GetCardInfoAsync(id))
                .ReturnsAsync(new CardInfo { Id = id, Name = $"Card {id}" });
        }

        List<Card> cards = await _service.ImportDeckFromYdkeAsync(code);

        Assert.That(cards.Select(card => card.ExternalCardId), Is.EqualTo(new int?[] { 123, 456, 789 }));
        Assert.That(cards.Select(card => card.Copies), Is.EqualTo(new[] { 2, 2, 1 }));
        Assert.That(cards.Select(card => card.Name), Is.EqualTo(new[] { "Card 123", "Card 456", "Card 789" }));
        _cardInfoServiceMock.Verify(service => service.GetCardInfoAsync(123), Times.Once);
        _cardInfoServiceMock.Verify(service => service.GetCardInfoAsync(456), Times.Once);
        _cardInfoServiceMock.Verify(service => service.GetCardInfoAsync(789), Times.Once);
        _cardInfoServiceMock.Verify(service => service.GetCardInfoAsync(999), Times.Never);
        _cardInfoServiceMock.Verify(service => service.GetCardInfoAsync(888), Times.Never);
    }

    [Test]
    public async Task ImportDeckFromYdkeAsync_CardInfoFailureUsesNumericFallback()
    {
        _cardInfoServiceMock
            .Setup(service => service.GetCardInfoAsync(123))
            .ThrowsAsync(new HttpRequestException("API unavailable"));

        List<Card> cards = await _service.ImportDeckFromYdkeAsync(BuildYdke([123]));

        Assert.That(cards, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
            {
                Assert.That(cards[0].Name, Is.EqualTo("123"));
                Assert.That(cards[0].ExternalCardId, Is.EqualTo(123));
                Assert.That(cards[0].Categories, Is.Empty);
                Assert.That(cards[0].ManualMetadataCategoryKeys, Is.Empty);
            }
        );
    }

    [Test]
    public void ImportDeckFromYdkeAsync_MalformedInputFailsBeforeCardLookup()
    {
        Assert.ThrowsAsync<FormatException>(async () =>
            await _service.ImportDeckFromYdkeAsync("ydke://o6lXBZyFNAI=!viOnAg==!*!")
        );

        _cardInfoServiceMock.Verify(service => service.GetCardInfoAsync(It.IsAny<int>()), Times.Never);
    }

    [Test]
    public async Task ImportDeckFromYdkAndYdkeAsync_EquivalentMainDecksProduceEquivalentCards()
    {
        int[] ids = [123, 456, 123, 789, 456];
        _fileServiceMock
            .Setup(service => service.ReadAllLinesAsync(It.IsAny<IBrowserFile>()))
            .ReturnsAsync(["#main", "123", "456", "123", "789", "456", "#extra", "999"]);

        foreach (int id in new[] { 123, 456, 789 })
        {
            _cardInfoServiceMock
                .Setup(service => service.GetCardInfoAsync(id))
                .ReturnsAsync(new CardInfo
                    {
                        Id = id, Name = $"Card {id}", Type = "Effect Monster",
                        Race = "Warrior", Attribute = "EARTH", Level = 4
                    }
                );
        }

        List<Card> ydkCards = await _service.ImportDeckFromYdkAsync(Mock.Of<IBrowserFile>());
        List<Card> ydkeCards =
            await _service.ImportDeckFromYdkeAsync(BuildYdke([123, 456, 123, 789, 456], [999], [888]));

        Assert.That(ydkeCards.Select(card => card.ExternalCardId),
            Is.EqualTo(ydkCards.Select(card => card.ExternalCardId))
        );
        Assert.That(ydkeCards.Select(card => card.Copies), Is.EqualTo(ydkCards.Select(card => card.Copies)));
        Assert.That(ydkeCards.Select(card => card.Name), Is.EqualTo(ydkCards.Select(card => card.Name)));
        Assert.That(ydkeCards.Select(card => card.Active), Is.EqualTo(ydkCards.Select(card => card.Active)));
        Assert.That(ydkeCards.All(card => card.ManualMetadataCategoryKeys.Count == 0), Is.True);
        Assert.That(ydkCards.All(card => card.ManualMetadataCategoryKeys.Count == 0), Is.True);

        for (int index = 0; index < ids.Distinct().Count(); index++)
        {
            Assert.That(ydkeCards[index].Categories.Select(category => category.Identity),
                Is.EqualTo(ydkCards[index].Categories.Select(category => category.Identity))
            );
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ArtworkIdentitySurvivesBothImportFormatsAndSessionCardRoundTrip(bool ydke)
    {
        foreach (int id in new[] { 123, 456 })
        {
            CardInfo info = new()
            {
                Id = id, CanonicalCardId = 123, Name = "Same named card", Type = "Spell Card",
                ArtworkMetadataKnown = true, ArtworkImageIds = new[] { 123, 456 }
            };
            _cardInfoServiceMock.Setup(s => s.GetCardInfoAsync(id)).ReturnsAsync(info);
            _cardInfoServiceMock.Setup(s => s.GetCardArtworkInfoAsync(id)).ReturnsAsync(info);
        }

        _fileServiceMock
            .Setup(s => s.ReadAllLinesAsync(It.IsAny<IBrowserFile>()))
            .ReturnsAsync(["#main", "123", "456", "123", "#extra"]);
        List<Card> cards = ydke
            ? await _service.ImportDeckFromYdkeAsync(BuildYdke([123, 456, 123]))
            : await _service.ImportDeckFromYdkAsync(Mock.Of<IBrowserFile>());
        Assert.That(cards.Select(c => c.ExternalCardId), Is.EqualTo(new int?[] { 123, 456 }));
        Assert.That(cards.Select(c => c.Copies), Is.EqualTo(new[] { 2, 1 }));
        Assert.That(cards[0].Id, Is.Not.EqualTo(cards[1].Id));
        _cardInfoServiceMock.Verify(s => s.GetCardArtworkInfoAsync(It.IsAny<int>()), Times.Never);
        JsonSerializerOptions options = new() { Converters = { new CardConverter(), new CategoryBaseConverter() } };
        string json = JsonSerializer.Serialize(cards, options);
        Assert.That(json,
            Does.Not.Contain("Artwork").And.Not.Contain("image").And.Not.Contain("http").And.Not.Contain("base64")
        );
        List<Card> loaded = JsonSerializer.Deserialize<List<Card>>(json, options)!;
        CardArtworkService artwork = new(_cardInfoServiceMock.Object);

        for (int i = 0; i < cards.Count; i++)
        {
            Assert.That((loaded[i].Id, loaded[i].ExternalCardId, loaded[i].Copies, loaded[i].Name),
                Is.EqualTo((cards[i].Id, cards[i].ExternalCardId, cards[i].Copies, cards[i].Name))
            );
            Assert.That(await artwork.GetArtworkUrlAsync(loaded[i].ExternalCardId!.Value),
                Is.EqualTo($"{CardArtworkService.ArtworkOrigin}/small/{loaded[i].ExternalCardId}.jpg")
            );
        }

        Card legacy = JsonSerializer.Deserialize<Card>("""{"Categories":[],"Copies":1,"Name":"Manual"}""", options)!;
        Assert.That(legacy.ExternalCardId, Is.Null);
    }

    private static string BuildYdke(uint[] main, uint[]? extra = null, uint[]? side = null) =>
        $"ydke://{Encode(main)}!{Encode(extra ?? [])}!{Encode(side ?? [])}!";

    private static string Encode(uint[] ids)
    {
        byte[] bytes = new byte[ids.Length * sizeof(uint)];

        for (int index = 0; index < ids.Length; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(index * sizeof(uint), sizeof(uint)), ids[index]);
        }

        return Convert.ToBase64String(bytes);
    }
}
