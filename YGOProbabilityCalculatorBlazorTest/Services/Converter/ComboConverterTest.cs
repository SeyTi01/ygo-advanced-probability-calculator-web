using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Converter;

namespace YGOProbabilityCalculatorBlazorTest.Services.Converter;

[TestFixture]
public class ComboConverterTests {
    private JsonSerializerOptions _options = null!;
    private ComboConverter _converter = null!;

    [SetUp]
    public void Setup() {
        _converter = new ComboConverter();
        _options = new JsonSerializerOptions {
            Converters = {
                _converter,
                new ComboCategoryConverter(),
                new CategoryBaseConverter()
            }
        };
    }

    [Test]
    public void Serialize_ValidCombo_ReturnsCorrectJson() {
        var baseCategory = new CategoryBase("TestCategory");
        var comboCategory = new ComboCategory(baseCategory, 1, 3);
        var combo = new Combo([comboCategory]);

        var json = JsonSerializer.Serialize(combo, _options);

        const string expectedJson =
            "{\"Categories\":[{\"BaseCategory\":{\"Name\":\"TestCategory\"},\"MinCount\":1,\"MaxCount\":3}]}";
        Assert.That(json, Is.EqualTo(expectedJson));
    }

    [Test]
    public void Serialize_ComboWithName_ReturnsCorrectJson() {
        var baseCategory = new CategoryBase("TestCategory");
        var comboCategory = new ComboCategory(baseCategory, 1, 3);
        var combo = new Combo([comboCategory], "Test Combo");

        var json = JsonSerializer.Serialize(combo, _options);

        const string expectedJson =
            "{\"Categories\":[{\"BaseCategory\":{\"Name\":\"TestCategory\"},\"MinCount\":1,\"MaxCount\":3}],\"Name\":\"Test Combo\"}";
        Assert.That(json, Is.EqualTo(expectedJson));
    }

    [Test]
    public void Deserialize_ValidJson_ReturnsCombo() {
        const string json =
            "{\"Categories\":[{\"BaseCategory\":{\"Name\":\"TestCategory\"},\"MinCount\":1,\"MaxCount\":3}]}";

        var combo = JsonSerializer.Deserialize<Combo>(json, _options);

        Assert.That(combo, Is.Not.Null);
        Assert.Multiple(() => {
            Assert.That(combo!.Categories, Has.Count.EqualTo(1));
            Assert.That(combo.Categories[0].BaseCategory.Name, Is.EqualTo("TestCategory"));
            Assert.That(combo.Categories[0].MinCount, Is.EqualTo(1));
            Assert.That(combo.Categories[0].MaxCount, Is.EqualTo(3));
            Assert.That(combo.Name, Is.Null);
        });
    }

    [Test]
    public void Deserialize_EmptyCategories_ReturnsComboWithEmptyCategories() {
        const string json = "{\"Categories\":[]}";

        var combo = JsonSerializer.Deserialize<Combo>(json, _options);

        Assert.That(combo, Is.Not.Null);
        Assert.Multiple(() => {
            Assert.That(combo!.Categories, Is.Empty);
            Assert.That(combo.Name, Is.Null);
        });
    }
}
