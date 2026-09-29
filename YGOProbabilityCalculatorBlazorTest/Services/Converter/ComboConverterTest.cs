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
            "{\"Categories\":[{\"BaseCategory\":{\"Name\":\"TestCategory\"},\"MinCount\":1,\"MaxCount\":3}],\"Active\":true}";
        Assert.That(json, Is.EqualTo(expectedJson));
    }

    [Test]
    public void Serialize_ComboWithName_ReturnsCorrectJson() {
        var baseCategory = new CategoryBase("TestCategory");
        var comboCategory = new ComboCategory(baseCategory, 1, 3);
        var combo = new Combo([comboCategory], "Test Combo");

        var json = JsonSerializer.Serialize(combo, _options);

        const string expectedJson =
            "{\"Categories\":[{\"BaseCategory\":{\"Name\":\"TestCategory\"},\"MinCount\":1,\"MaxCount\":3}],\"Name\":\"Test Combo\",\"Active\":true}";
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

    [Test]
    public void GroupMembershipAndInactiveStateRoundTripWithoutChangingLegacyJson() {
        var combo = new Combo([], "Grouped", false, "stable-group-id");
        var json = JsonSerializer.Serialize(combo, _options);
        Assert.That(json, Does.Contain("\"GroupId\":\"stable-group-id\"").And.Contain("\"Active\":false"));
        var loaded = JsonSerializer.Deserialize<Combo>(json, _options)!;
        Assert.That(loaded.GroupId, Is.EqualTo("stable-group-id"));
        Assert.That(loaded.Active, Is.False);
        Assert.That(loaded.WithName("Renamed").GroupId, Is.EqualTo("stable-group-id"));
        Assert.That(loaded.WithCategories([]).GroupId, Is.EqualTo("stable-group-id"));

        var legacy = JsonSerializer.Deserialize<Combo>("{\"Categories\":[]}", _options)!;
        Assert.That(legacy.GroupId, Is.Null);
        Assert.That(legacy.Active, Is.True);
    }

    [Test]
    public void MixedRequirementsRoundTripThroughAllReplacementOperations() {
        var card = new Card([], 2, "Card");
        var combo = new Combo([new(new CategoryBase("Role"), 0, 2)], "Mixed", true, "g",
            [new(card.Id, 1, 2)]);
        var loaded = JsonSerializer.Deserialize<Combo>(JsonSerializer.Serialize(combo, _options), _options)!;
        Assert.That(loaded.Cards.Single().CardId, Is.EqualTo(card.Id));
        Assert.That(loaded.Cards.Single().MinCount, Is.EqualTo(1));
        Assert.That(loaded.Categories, Has.Count.EqualTo(1));
        Assert.That(loaded.WithName("Other").WithActive(false).WithGroup(null).WithCategories([]).Cards,
            Has.Count.EqualTo(1));
        Assert.That(loaded.WithCards([]).Categories, Has.Count.EqualTo(1));
    }
}
