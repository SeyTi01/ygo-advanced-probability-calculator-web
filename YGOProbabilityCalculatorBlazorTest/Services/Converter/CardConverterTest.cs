using System.Text.Json;
using System.Text.Json.Nodes;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Converter;

namespace YGOProbabilityCalculatorBlazorTest.Services.Converter;

[TestFixture]
public class CardConverterTests
{
    private JsonSerializerOptions _options = null!;
    private CardConverter _converter = null!;

    [SetUp]
    public void Setup()
    {
        _converter = new CardConverter();
        _options = new JsonSerializerOptions
        {
            Converters = { _converter, new CategoryBaseConverter() }
        };
    }

    [Test]
    public void Serialize_ValidCard_ReturnsCorrectJson()
    {
        List<CategoryBase> categories = [new("Category1")];
        Card card = new(categories, 3, "TestCard");

        string json = JsonSerializer.Serialize(card, _options);

        string expectedJson =
            $"{{\"Categories\":[{{\"Name\":\"Category1\",\"Source\":\"User\"}}],\"Copies\":3,\"Name\":\"TestCard\",\"Active\":true,\"Id\":\"{card.Id}\",\"ManualMetadataCategoryKeys\":[]}}";
        Assert.That(json, Is.EqualTo(expectedJson));
    }

    [Test]
    public void Deserialize_ValidJson_ReturnsCard()
    {
        const string json = "{\"Categories\":[{\"Name\":\"Category1\"}],\"Copies\":3,\"Name\":\"TestCard\"}";

        Card? card = JsonSerializer.Deserialize<Card>(json, _options);

        Assert.That(card, Is.Not.Null);
        Assert.That(card.Categories.First().Name, Is.EqualTo("Category1"));
        Assert.Multiple(() =>
            {
                Assert.That(card!.Copies, Is.EqualTo(3));
                Assert.That(card.Name, Is.EqualTo("TestCard"));
                Assert.That(card.Categories, Has.Count.EqualTo(1));
            }
        );
    }

    [Test]
    public void Deserialize_EmptyCategories_ReturnsCardWithEmptyCategories()
    {
        const string json = "{\"Categories\":[],\"Copies\":1,\"Name\":\"TestCard\"}";

        Card? card = JsonSerializer.Deserialize<Card>(json, _options);

        Assert.That(card, Is.Not.Null);
        Assert.That(card!.Categories, Is.Empty);
    }

    [Test]
    public void CardIdentityRoundTripsAndLegacyCardsGetDifferentIds()
    {
        Card card = new([], 2, "Twin");
        Card loaded = JsonSerializer.Deserialize<Card>(JsonSerializer.Serialize(card, _options), _options)!;
        Assert.That(loaded.Id, Is.EqualTo(card.Id));
        Assert.That(loaded.WithName("Renamed").WithActive(false).Id, Is.EqualTo(card.Id));
        const string legacy = "{\"Categories\":[],\"Copies\":1,\"Name\":\"Twin\"}";
        Card first = JsonSerializer.Deserialize<Card>(legacy, _options)!;
        Card second = JsonSerializer.Deserialize<Card>(legacy, _options)!;
        Assert.That(first.Id, Is.Not.Empty.And.Not.EqualTo(second.Id));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Card>(
                "{\"Categories\":[],\"Copies\":1,\"Name\":null,\"Id\":\"\"}",
                _options
            )
        );
    }

    [Test]
    public void ManualPropertiesRoundTripAndMissingFieldMeansObjectiveMembership()
    {
        CategoryBase fire = new("Attribute: FIRE", CategorySource.Metadata, "attribute:fire");
        Card card = new Card([], 3, "ROTA", false, "rota", 32807846).WithManualMetadataCategory(fire);
        string json = JsonSerializer.Serialize(card, _options);
        Card loaded = JsonSerializer.Deserialize<Card>(json, _options)!;
        Assert.That(loaded.ManualMetadataCategoryKeys, Is.EquivalentTo(new[] { fire.MetadataKey }));
        Assert.That(loaded.Categories, Is.EqualTo(card.Categories));
        Assert.That((loaded.Id, loaded.ExternalCardId, loaded.Copies, loaded.Name, loaded.Active),
            Is.EqualTo((card.Id, card.ExternalCardId, card.Copies, card.Name, card.Active))
        );
        JsonNode previewV2 = JsonNode.Parse(json)!;
        previewV2.AsObject().Remove("ManualMetadataCategoryKeys");
        Card preview = JsonSerializer.Deserialize<Card>(previewV2.ToJsonString(), _options)!;
        Assert.That(preview.ManualMetadataCategoryKeys, Is.Empty);
        Assert.That(preview.WithoutManualMetadataCategory(fire.MetadataKey!).Categories, Does.Contain(fire));
    }

    [TestCase("null")]
    [TestCase("\"attribute:fire\"")]
    [TestCase("[null]")]
    [TestCase("[\"\"]")]
    [TestCase("[\"attribute:water\"]")]
    [TestCase("[123]")]
    public void MalformedManualPropertiesAreRejected(string keys)
    {
        string json = """
                      {"Categories":[{"Name":"Attribute: FIRE","Source":"Metadata","MetadataKey":"attribute:fire"}],
                       "Copies":1,"Name":"ROTA","ManualMetadataCategoryKeys":
                      """ + keys + "}";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Card>(json, _options));
    }

    [Test]
    public void PersistedDuplicateManualKeysAreNormalized()
    {
        string json = """
                      {"Categories":[{"Name":"Attribute: FIRE","Source":"Metadata","MetadataKey":"attribute:fire"}],
                       "Copies":1,"Name":"ROTA","ManualMetadataCategoryKeys":["attribute:fire","attribute:fire"]}
                      """;
        Assert.That(JsonSerializer.Deserialize<Card>(json, _options)!.ManualMetadataCategoryKeys, Has.Count.EqualTo(1));
    }
}
