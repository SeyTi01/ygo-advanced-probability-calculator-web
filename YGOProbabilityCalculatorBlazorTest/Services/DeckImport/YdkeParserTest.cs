using System.Buffers.Binary;
using YGOProbabilityCalculatorBlazor.Services.DeckImport;

namespace YGOProbabilityCalculatorBlazorTest.Services.DeckImport;

public class YdkeParserTest
{
    [Test]
    public void Parse_CanonicalFixtureDecodesAllSections()
    {
        YdkeDeck deck = YdkeParser.Parse("ydke://o6lXBZyFNAI=!viOnAg==!7ydRAA==!");

        Assert.Multiple(() =>
        {
            Assert.That(deck.MainDeck, Is.EqualTo(new uint[] { 89631139, 36996508 }));
            Assert.That(deck.ExtraDeck, Is.EqualTo(new uint[] { 44508094 }));
            Assert.That(deck.SideDeck, Is.EqualTo(new uint[] { 5318639 }));
        });
    }

    [Test]
    public void Parse_MainOnlyCodeAllowsEmptyExtraAndSideSections()
    {
        YdkeDeck deck = YdkeParser.Parse("ydke://o6lXBZyFNAI=!!!");

        Assert.That(deck.MainDeck, Is.EqualTo(new uint[] { 89631139, 36996508 }));
        Assert.That(deck.ExtraDeck, Is.Empty);
        Assert.That(deck.SideDeck, Is.Empty);
    }

    [Test]
    public void Parse_PreservesDuplicateIds()
    {
        YdkeDeck deck = YdkeParser.Parse(BuildCode([123, 123, 456]));

        Assert.That(deck.MainDeck, Is.EqualTo(new uint[] { 123, 123, 456 }));
    }

    [Test]
    public void Parse_TrimsSurroundingWhitespaceAndRecognizesSchemeCaseInsensitively()
    {
        YdkeDeck deck = YdkeParser.Parse("  YDKE://o6lXBZyFNAI=!!! \r\n");

        Assert.That(deck.MainDeck, Is.EqualTo(new uint[] { 89631139, 36996508 }));
    }

    [TestCase("o6lXBZyFNAI=!viOnAg==!7ydRAA==!")]
    [TestCase("https://o6lXBZyFNAI=!viOnAg==!7ydRAA==!")]
    public void Parse_MissingOrUnsupportedSchemeThrows(string code)
    {
        Assert.Throws<FormatException>(() => YdkeParser.Parse(code));
    }

    [TestCase("ydke://o6lXBZyFNAI=")]
    [TestCase("ydke://o6lXBZyFNAI=!")]
    [TestCase("ydke://o6lXBZyFNAI=!viOnAg==!")]
    public void Parse_MissingSeparatorsOrSectionsThrows(string code)
    {
        Assert.Throws<FormatException>(() => YdkeParser.Parse(code));
    }

    [TestCase("ydke://*!!!")]
    [TestCase("ydke://o6lXBZyFNAI=!*!!")]
    [TestCase("ydke://o6lXBZyFNAI=!!*!")]
    public void Parse_InvalidBase64InAnySectionThrows(string code)
    {
        Assert.Throws<FormatException>(() => YdkeParser.Parse(code));
    }

    [Test]
    public void Parse_RejectsSectionWhoseDecodedLengthIsNotDivisibleByFour()
    {
        Assert.Throws<FormatException>(() => YdkeParser.Parse("ydke://AQ==!!!"));
    }

    [TestCase("ydke://o6lXBZyFNAI=!viOnAg==!7ydRAA==!garbage")]
    [TestCase("ydke://o6lXBZyFNAI=!viOnAg==!7ydRAA==!!")]
    public void Parse_RejectsTrailingOrExtraStructuralGarbage(string code)
    {
        Assert.Throws<FormatException>(() => YdkeParser.Parse(code));
    }

    [Test]
    public void Parse_ReadsPasscodesAsExplicitLittleEndianUInt32Values()
    {
        YdkeDeck deck = YdkeParser.Parse("ydke://AQIDBA==!!!");

        Assert.That(deck.MainDeck, Is.EqualTo(new uint[] { 0x04030201 }));
    }

    private static string BuildCode(uint[] main)
    {
        byte[] bytes = new byte[main.Length * sizeof(uint)];

        for (int index = 0; index < main.Length; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(index * sizeof(uint), sizeof(uint)), main[index]);
        }

        return $"ydke://{Convert.ToBase64String(bytes)}!!!";
    }
}
