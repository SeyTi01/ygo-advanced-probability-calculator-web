using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Converter;

namespace YGOProbabilityCalculatorBlazorTest.Services.Converter;

[TestFixture]
public class StrictCardFieldReaderCompatibilityTests {
    private JsonSerializerOptions _options = null!;

    [SetUp]
    public void Setup() {
        _options = new JsonSerializerOptions {
            Converters = { new CardConverter(), new CategoryBaseConverter() }
        };
    }

    [TestCase("\"DrawCount\":1,\"drawcount\":2")]
    [TestCase("\"SimulationEnabled\":true,\"simulationenabled\":false")]
    [TestCase("\"OncePerTurn\":true,\"onceperturn\":false")]
    public void Deserialize_CaseInsensitiveDuplicateFields_RejectsDocument(string duplicateFields) {
        string json = $"{{\"Categories\":[],\"Copies\":1,\"Name\":\"Test\",{duplicateFields}}}";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Card>(json, _options));
    }

    [Test]
    public void Deserialize_DrawCountNullAndAbsenceBothKeepLegacyNoDrawDefault() {
        Card absent = Deserialize("\"Name\":\"Test\"");
        Card explicitNull = Deserialize("\"Name\":\"Test\",\"DrawCount\":null");

        Assert.That(absent.DrawCount, Is.Null);
        Assert.That(explicitNull.DrawCount, Is.Null);
    }

    [TestCase("0")]
    [TestCase("4")]
    [TestCase("true")]
    public void Deserialize_InvalidDrawCount_RejectsValue(string drawCount) {
        string json = $"{{\"Categories\":[],\"Copies\":1,\"Name\":\"Test\",\"DrawCount\":{drawCount}}}";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Card>(json, _options));
    }

    [Test]
    public void Deserialize_ExplicitNullBoolean_IsRejectedWhileMissingFieldKeepsDefault() {
        Card absent = Deserialize("\"Name\":\"Test\"");

        Assert.That(absent.SimulationEnabled, Is.True);
        Assert.Throws<JsonException>(() => Deserialize("\"Name\":\"Test\",\"SimulationEnabled\":null"));
    }

    private Card Deserialize(string fields) {
        string json = $"{{\"Categories\":[],\"Copies\":1,{fields}}}";
        return JsonSerializer.Deserialize<Card>(json, _options)!;
    }
}
