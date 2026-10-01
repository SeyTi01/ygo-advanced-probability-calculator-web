using System.Buffers.Binary;

namespace YGOProbabilityCalculatorBlazor.Services.DeckImport;

public sealed record YdkeDeck(
    IReadOnlyList<uint> MainDeck,
    IReadOnlyList<uint> ExtraDeck,
    IReadOnlyList<uint> SideDeck);

public static class YdkeParser {
    private const string Scheme = "ydke://";

    public static YdkeDeck Parse(string code) {
        ArgumentNullException.ThrowIfNull(code);
        var trimmed = code.Trim();
        if (!trimmed.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase))
            throw new FormatException("YDKe code must start with 'ydke://'.");

        var sections = trimmed[Scheme.Length..].Split('!');
        if (sections.Length != 4 || sections[3].Length != 0)
            throw new FormatException("YDKe code must contain exactly three Base64 sections, each followed by '!'.");

        if (sections[0].Length == 0)
            throw new FormatException("YDKe code must include a main-deck section.");

        return new YdkeDeck(
            DecodeSection(sections[0], "main"),
            DecodeSection(sections[1], "extra"),
            DecodeSection(sections[2], "side"));
    }

    private static IReadOnlyList<uint> DecodeSection(string encoded, string sectionName) {
        byte[] bytes;
        try {
            bytes = Convert.FromBase64String(encoded);
        }
        catch (FormatException exception) {
            throw new FormatException($"The YDKe {sectionName}-deck section is not valid Base64.", exception);
        }

        if (bytes.Length % sizeof(uint) != 0)
            throw new FormatException($"The decoded YDKe {sectionName}-deck section length must be divisible by four bytes.");

        var cardIds = new uint[bytes.Length / sizeof(uint)];
        for (var index = 0; index < cardIds.Length; index++) {
            cardIds[index] = BinaryPrimitives.ReadUInt32LittleEndian(
                bytes.AsSpan(index * sizeof(uint), sizeof(uint)));
        }

        return Array.AsReadOnly(cardIds);
    }
}
