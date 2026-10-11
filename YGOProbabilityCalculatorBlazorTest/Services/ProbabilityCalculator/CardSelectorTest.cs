using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public class CardSelectorTest {
    [Test]
    public void PostDrawSelectorIsImmutableNormalizedAndIdentityBased() {
        CategoryBase first = new("Same", CategorySource.Metadata, "first");
        CategoryBase second = new("Same", CategorySource.Metadata, "second");
        List<string> identities = [second.Identity, first.Identity, second.Identity];
        CardSelector selector = CardSelector.AnyOf(identities);
        identities.Clear();
        Assert.That(selector, Is.EqualTo(CardSelector.AnyOf([first.Identity, second.Identity])));
        Assert.That(selector.GetHashCode(), Is.EqualTo(CardSelector.AnyOf([first.Identity, second.Identity]).GetHashCode()));
        Assert.That(CardSelector.AnyOf([first.Identity, first.Identity]), Is.EqualTo(CardSelector.For(first)));
        List<Card> cards = [new([first, second]), new([first]), new([new("Same")]), new([])];
        Assert.That(CardSelectorCompiler.Compile(selector, cards, new(CalculationWorkPolicy.Interactive)), Is.EqualTo(new[] { true, true, false, false }));
        Assert.That(CardSelectorCompiler.Compile(CardSelector.For(second), cards, new(CalculationWorkPolicy.Interactive)), Is.EqualTo(new[] { true, false, false, false }));
        Assert.That(CardSelectorCompiler.Compile(CardSelector.Any, cards, new(CalculationWorkPolicy.Interactive)), Is.All.True);
        Assert.That(JsonSerializer.Deserialize<CardSelector>(JsonSerializer.Serialize(selector)), Is.EqualTo(selector));
    }

    [TestCase("null")]
    [TestCase("{}")]
    [TestCase("{\"Kind\":\"AnyOf\",\"Categories\":[]}")]
    [TestCase("{\"Kind\":\"Not\",\"Categories\":[\"user:A\"]}")]
    [TestCase("{\"Kind\":\"Category\",\"Categories\":[\"A\"]}")]
    [TestCase("{\"Kind\":\"Category\",\"Categories\":[null]}")]
    [TestCase("{\"Kind\":\"Category\",\"Categories\":[\"user: \" ]}")]
    [TestCase("{\"Kind\":\"Any\",\"Categories\":[\"user:A\"]}")]
    [TestCase("{\"Kind\":\"Category\",\"Categories\":[\"user:A\",\"user:B\"]}")]
    [TestCase("{\"Kind\":\"Any\",\"Kind\":\"Category\",\"Categories\":[]}")]
    [TestCase("{\"Kind\":\"Any\",\"Categories\":[],\"Other\":true}")]
    public void PostDrawSelectorRejectsMalformedWirePayloads(string json) {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CardSelector>(json));
    }

    [Test]
    public void PostDrawSelectorCompilationObeysRequestStorage() {
        CardSelector selector = CardSelector.AnyOf(Enumerable.Range(0, 262145).Select(index => $"user:{index}"));
        ProbabilityCalculationLimitException? error = Assert.Throws<ProbabilityCalculationLimitException>(() =>
            CardSelectorCompiler.Compile(selector, [new([])], new(CalculationWorkPolicy.Interactive)));
        Assert.That(error!.Reason, Is.EqualTo(ProbabilityCalculationLimitReason.Storage));
        Assert.Throws<ArgumentException>(() => CardSelector.AnyOf([]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CardSelector((CardSelectorKind)99, []));
    }
}
