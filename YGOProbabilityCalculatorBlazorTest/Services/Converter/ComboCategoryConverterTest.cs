using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Converter;

namespace YGOProbabilityCalculatorBlazorTest.Services.Converter;

[TestFixture]
public class ComboCategoryConverterTests {
    private JsonSerializerOptions _options = null!;
    private ComboCategoryConverter _converter = null!;

    [SetUp]
    public void Setup() {
        _converter = new ComboCategoryConverter();
        _options = new JsonSerializerOptions {
            Converters = { _converter, new CategoryBaseConverter() }
        };
    }

    [Test]
    public void Serialize_ValidComboCategory_ReturnsCorrectJson() {
        var baseCategory = new CategoryBase("TestCategory");
        var comboCategory = new ComboCategory(baseCategory, 1, 3);

        var json = JsonSerializer.Serialize(comboCategory, _options);

        const string expectedJson = "{\"BaseCategory\":{\"Name\":\"TestCategory\"},\"MinCount\":1,\"MaxCount\":3}";
        Assert.That(json, Is.EqualTo(expectedJson));
    }

    [Test]
    public void Deserialize_ValidJson_ReturnsComboCategory() {
        const string json = "{\"BaseCategory\":{\"Name\":\"TestCategory\"},\"MinCount\":1,\"MaxCount\":3}";

        var comboCategory = JsonSerializer.Deserialize<ComboCategory>(json, _options);

        Assert.That(comboCategory, Is.Not.Null);
        Assert.Multiple(() => {
            Assert.That(comboCategory!.BaseCategory.Name, Is.EqualTo("TestCategory"));
            Assert.That(comboCategory.MinCount, Is.EqualTo(1));
            Assert.That(comboCategory.MaxCount, Is.EqualTo(3));
        });
    }

    [Test]
    public void Deserialize_MissingBaseCategory_ThrowsKeyNotFoundException() {
        const string json = "{\"MinCount\":1,\"MaxCount\":3}";

        Assert.Throws<KeyNotFoundException>(() => JsonSerializer.Deserialize<ComboCategory>(json, _options));
    }
}
