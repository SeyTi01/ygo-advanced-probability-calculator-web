using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazorTest.Components;

[TestFixture]
public class CardPropertyColorPaletteTest {
    [TestCase("attribute:fire", "card-property-color-attribute-fire")]
    [TestCase("attribute:water", "card-property-color-attribute-water")]
    [TestCase("attribute:wind", "card-property-color-attribute-wind")]
    [TestCase("attribute:earth", "card-property-color-attribute-earth")]
    [TestCase("attribute:light", "card-property-color-attribute-light")]
    [TestCase("attribute:dark", "card-property-color-attribute-dark")]
    [TestCase("attribute:divine", "card-property-color-attribute-divine")]
    [TestCase("attribute:future", "card-property-color-attribute")]
    [TestCase("kind:spell", "card-property-color-spell")]
    [TestCase("spell-type:normal", "card-property-color-spell")]
    [TestCase("kind:trap", "card-property-color-trap")]
    [TestCase("trap-type:counter", "card-property-color-trap")]
    [TestCase("kind:monster", "card-property-color-monster")]
    [TestCase("monster-trait:normal", "card-property-color-normal")]
    [TestCase("monster-trait:effect", "card-property-color-effect")]
    [TestCase("monster-type:ritual", "card-property-color-ritual")]
    [TestCase("monster-type:fusion", "card-property-color-fusion")]
    [TestCase("monster-type:synchro", "card-property-color-synchro")]
    [TestCase("monster-type:xyz", "card-property-color-xyz")]
    [TestCase("monster-type:link", "card-property-color-monster-link")]
    [TestCase("monster-trait:pendulum", "card-property-color-pendulum")]
    [TestCase("monster-trait:flip", "card-property-color-monster-trait")]
    [TestCase("monster-trait:tuner", "card-property-color-monster-trait")]
    [TestCase("monster-trait:gemini", "card-property-color-monster-trait")]
    [TestCase("monster-trait:spirit", "card-property-color-monster-trait")]
    [TestCase("monster-trait:toon", "card-property-color-monster-trait")]
    [TestCase("monster-trait:union", "card-property-color-monster-trait")]
    [TestCase("monster-trait:future", "card-property-color-monster-trait")]
    [TestCase("level:4", "card-property-color-level")]
    [TestCase("rank:4", "card-property-color-rank")]
    [TestCase("link:2", "card-property-color-link-rating")]
    [TestCase("scale:5", "card-property-color-scale")]
    [TestCase("monster-race:dragon", "card-property-color-monster-race")]
    [TestCase("archetype:k9", "card-property-color-archetype")]
    [TestCase("unrecognized:future", "card-property-color-generic")]
    [TestCase("malformed-key", "card-property-color-generic")]
    public void ResolvesSemanticColorFromMetadataKey(string metadataKey, string expectedClass) {
        Assert.That(CardPropertyColorPalette.GetCssClass(metadataKey), Is.EqualTo(expectedClass));
    }

    [Test]
    public void NumericRaceAndArchetypeValuesShareTheirSemanticFamilyColor() {
        Assert.That(CardPropertyColorPalette.GetCssClass("level:1"),
            Is.EqualTo(CardPropertyColorPalette.GetCssClass("level:12")));
        Assert.That(CardPropertyColorPalette.GetCssClass("rank:1"),
            Is.EqualTo(CardPropertyColorPalette.GetCssClass("rank:13")));
        Assert.That(CardPropertyColorPalette.GetCssClass("link:1"),
            Is.EqualTo(CardPropertyColorPalette.GetCssClass("link:6")));
        Assert.That(CardPropertyColorPalette.GetCssClass("scale:0"),
            Is.EqualTo(CardPropertyColorPalette.GetCssClass("scale:13")));
        Assert.That(CardPropertyColorPalette.GetCssClass("monster-race:warrior"),
            Is.EqualTo(CardPropertyColorPalette.GetCssClass("monster-race:dragon")));
        Assert.That(CardPropertyColorPalette.GetCssClass("archetype:k9"),
            Is.EqualTo(CardPropertyColorPalette.GetCssClass("archetype:vanquish%20soul")));
    }

    [Test]
    public void StableMetadataKeyDeterminesClassRegardlessOfDisplayName() {
        var fire = new CategoryBase("Something with an unrelated label", CategorySource.Metadata, "attribute:fire");
        var misleadingLabel = new CategoryBase("Attribute: WATER", CategorySource.Metadata, "attribute:fire");

        Assert.That(CardPropertyColorPalette.GetCssClass(fire), Is.EqualTo("card-property-color-attribute-fire"));
        Assert.That(CardPropertyColorPalette.GetCssClass(misleadingLabel), Is.EqualTo(CardPropertyColorPalette.GetCssClass(fire)));
        Assert.That(CardPropertyColorPalette.GetCssClass("future-family:water"), Is.EqualTo(CardPropertyColorPalette.GenericMetadataClass));
    }
}
