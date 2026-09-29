using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Converter;

namespace YGOProbabilityCalculatorBlazorTest.Services.Converter;

[TestFixture]
public class CardConverterTests {
    private JsonSerializerOptions _options = null!;
    private CardConverter _converter = null!;

    [SetUp]
    public void Setup() {
        _converter = new CardConverter();
        _options = new JsonSerializerOptions {
            Converters = { _converter, new CategoryBaseConverter() }
        };
    }

    [Test]
    public void Serialize_ValidCard_ReturnsCorrectJson() {
        var categories = new List<CategoryBase> { new("Category1") };
        var card = new Card(categories, 3, "TestCard");

        var json = JsonSerializer.Serialize(card, _options);

        var expectedJson = $"{{\"Categories\":[{{\"Name\":\"Category1\"}}],\"Copies\":3,\"Name\":\"TestCard\",\"Active\":true,\"Id\":\"{card.Id}\"}}";
        Assert.That(json, Is.EqualTo(expectedJson));
    }

    [Test]
    public void Deserialize_ValidJson_ReturnsCard() {
        const string json = "{\"Categories\":[{\"Name\":\"Category1\"}],\"Copies\":3,\"Name\":\"TestCard\"}";

        var card = JsonSerializer.Deserialize<Card>(json, _options);

        Assert.That(card, Is.Not.Null);
        Assert.That(card.Categories.First().Name, Is.EqualTo("Category1"));
        Assert.Multiple(() => {
            Assert.That(card!.Copies, Is.EqualTo(3));
            Assert.That(card.Name, Is.EqualTo("TestCard"));
            Assert.That(card.Categories, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void Deserialize_EmptyCategories_ReturnsCardWithEmptyCategories() {
        const string json = "{\"Categories\":[],\"Copies\":1,\"Name\":\"TestCard\"}";

        var card = JsonSerializer.Deserialize<Card>(json, _options);

        Assert.That(card, Is.Not.Null);
        Assert.That(card!.Categories, Is.Empty);
    }

    [Test]
    public void CardIdentityRoundTripsAndLegacyCardsGetDifferentIds() {
        var card = new Card([], 2, "Twin");
        var loaded = JsonSerializer.Deserialize<Card>(JsonSerializer.Serialize(card, _options), _options)!;
        Assert.That(loaded.Id, Is.EqualTo(card.Id));
        Assert.That(loaded.WithName("Renamed").WithActive(false).Id, Is.EqualTo(card.Id));
        const string legacy = "{\"Categories\":[],\"Copies\":1,\"Name\":\"Twin\"}";
        var first = JsonSerializer.Deserialize<Card>(legacy, _options)!;
        var second = JsonSerializer.Deserialize<Card>(legacy, _options)!;
        Assert.That(first.Id, Is.Not.Empty.And.Not.EqualTo(second.Id));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Card>(
            "{\"Categories\":[],\"Copies\":1,\"Name\":null,\"Id\":\"\"}", _options));
    }
}
