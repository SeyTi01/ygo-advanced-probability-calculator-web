using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Converter;

namespace YGOProbabilityCalculatorBlazorTest.Services.Converter;

[TestFixture]
public class ComboConverterTests
{
    private JsonSerializerOptions _options = null!;
    private ComboConverter _converter = null!;

    [SetUp]
    public void Setup()
    {
        _converter = new ComboConverter();
        _options = new JsonSerializerOptions
        {
            Converters = { _converter, new ComboCategoryConverter(), new CategoryBaseConverter() },
        };
    }

    [Test]
    public void Serialize_ValidCombo_ReturnsCorrectJson()
    {
        CategoryBase baseCategory = new("TestCategory");
        ComboCategory comboCategory = new(baseCategory, 1, 3);
        Combo combo = new([comboCategory]);

        string json = JsonSerializer.Serialize(combo, _options);

        const string expectedJson =
            "{\"Categories\":[{\"BaseCategory\":{\"Name\":\"TestCategory\",\"Source\":\"User\"},\"MinCount\":1,\"MaxCount\":3,\"MaximumMode\":\"Fixed\"}],\"Active\":true}";
        Assert.That(json, Is.EqualTo(expectedJson));
    }

    [Test]
    public void Serialize_ComboWithName_ReturnsCorrectJson()
    {
        CategoryBase baseCategory = new("TestCategory");
        ComboCategory comboCategory = new(baseCategory, 1, 3);
        Combo combo = new([comboCategory], "Test Combo");

        string json = JsonSerializer.Serialize(combo, _options);

        const string expectedJson =
            "{\"Categories\":[{\"BaseCategory\":{\"Name\":\"TestCategory\",\"Source\":\"User\"},\"MinCount\":1,\"MaxCount\":3,\"MaximumMode\":\"Fixed\"}],\"Name\":\"Test Combo\",\"Active\":true}";
        Assert.That(json, Is.EqualTo(expectedJson));
    }

    [Test]
    public void Deserialize_ValidJson_ReturnsCombo()
    {
        const string json =
            "{\"Categories\":[{\"BaseCategory\":{\"Name\":\"TestCategory\",\"Source\":\"User\"},\"MinCount\":1,\"MaxCount\":3}]}";

        Combo? combo = JsonSerializer.Deserialize<Combo>(json, _options);

        Assert.That(combo, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(combo!.Categories, Has.Count.EqualTo(1));
            Assert.That(combo.Categories[0].BaseCategory.Name, Is.EqualTo("TestCategory"));
            Assert.That(combo.Categories[0].MinCount, Is.EqualTo(1));
            Assert.That(combo.Categories[0].MaxCount, Is.EqualTo(3));
            Assert.That(combo.Name, Is.Null);
        });
    }

    [Test]
    public void Deserialize_EmptyCategories_ReturnsComboWithEmptyCategories()
    {
        const string json = "{\"Categories\":[]}";

        Combo? combo = JsonSerializer.Deserialize<Combo>(json, _options);

        Assert.That(combo, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(combo!.Categories, Is.Empty);
            Assert.That(combo.Name, Is.Null);
        });
    }

    [Test]
    public void GroupMembershipAndInactiveStateRoundTripWithoutChangingLegacyJson()
    {
        Combo combo = new([], "Grouped", false, "stable-group-id");
        string json = JsonSerializer.Serialize(combo, _options);
        Assert.That(
            json,
            Does.Contain("\"GroupId\":\"stable-group-id\"").And.Contain("\"Active\":false")
        );
        Combo loaded = JsonSerializer.Deserialize<Combo>(json, _options)!;
        Assert.That(loaded.GroupId, Is.EqualTo("stable-group-id"));
        Assert.That(loaded.Active, Is.False);
        Assert.That(loaded.WithName("Renamed").GroupId, Is.EqualTo("stable-group-id"));
        Assert.That(loaded.WithCategories([]).GroupId, Is.EqualTo("stable-group-id"));

        Combo legacy = JsonSerializer.Deserialize<Combo>("{\"Categories\":[]}", _options)!;
        Assert.That(legacy.GroupId, Is.Null);
        Assert.That(legacy.Active, Is.True);
    }

    [Test]
    public void MixedRequirementsRoundTripThroughAllReplacementOperations()
    {
        Card card = new([], 2, "Card");
        Combo combo = new(
            [new(new CategoryBase("Role"), 0, 2)],
            "Mixed",
            true,
            "g",
            [new(card.Id, 1, 2)]
        );
        Combo loaded = JsonSerializer.Deserialize<Combo>(
            JsonSerializer.Serialize(combo, _options),
            _options
        )!;
        Assert.That(loaded.Cards.Single().CardId, Is.EqualTo(card.Id));
        Assert.That(loaded.Cards.Single().MinCount, Is.EqualTo(1));
        Assert.That(loaded.Categories, Has.Count.EqualTo(1));
        Assert.That(
            loaded.WithName("Other").WithActive(false).WithGroup(null).WithCategories([]).Cards,
            Has.Count.EqualTo(1)
        );
        Assert.That(loaded.WithCards([]).Categories, Has.Count.EqualTo(1));
    }
}
