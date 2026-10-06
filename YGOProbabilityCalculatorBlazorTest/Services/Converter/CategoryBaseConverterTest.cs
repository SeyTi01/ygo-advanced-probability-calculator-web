using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Converter;

namespace YGOProbabilityCalculatorBlazorTest.Services.Converter;

[TestFixture]
public class CategoryBaseConverterTests
{
    private JsonSerializerOptions _options = null!;
    private CategoryBaseConverter _converter = null!;

    [SetUp]
    public void Setup()
    {
        _converter = new CategoryBaseConverter();
        _options = new JsonSerializerOptions { Converters = { _converter } };
    }

    [Test]
    public void Serialize_ValidCategoryBase_ReturnsCorrectJson()
    {
        CategoryBase category = new("TestCategory");

        string json = JsonSerializer.Serialize(category, _options);

        Assert.That(json, Is.EqualTo("{\"Name\":\"TestCategory\",\"Source\":\"User\"}"));
    }

    [Test]
    public void Deserialize_ValidJson_ReturnsCategoryBase()
    {
        const string json = "{\"Name\":\"TestCategory\"}";

        CategoryBase? category = JsonSerializer.Deserialize<CategoryBase>(json, _options);

        Assert.That(category, Is.Not.Null);
        Assert.That(category!.Name, Is.EqualTo("TestCategory"));
    }

    [Test]
    public void Deserialize_MissingName_ThrowsKeyNotFoundException()
    {
        const string json = "{}";

        Assert.Throws<KeyNotFoundException>(() =>
            JsonSerializer.Deserialize<CategoryBase>(json, _options)
        );
    }

    [Test]
    public void Deserialize_NullName_ThrowsJsonException()
    {
        const string json = "{\"Name\":null}";

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<CategoryBase>(json, _options)
        );
    }

    [TestCase("{\"Name\":\"Spell\",\"Source\":\"Metadata\"}")]
    [TestCase("{\"Name\":\"Spell\",\"Source\":\"Metadata\",\"MetadataKey\":\"\"}")]
    [TestCase("{\"Name\":\"Spell\",\"Source\":\"Unknown\"}")]
    [TestCase("{\"Name\":\"Spell\",\"Source\":42}")]
    public void InvalidCategoryIdentityIsRejected(string json) =>
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<CategoryBase>(json, _options)
        );
}
