using System.Globalization;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.DeckImport;

public static class CardPropertyProvider
{
    // Tokens correspond to the documented API type vocabulary, including combined types.
    private static readonly string[] MonsterFacets =
    [
        "Normal", "Effect", "Flip", "Tuner", "Gemini", "Pendulum", "Ritual",
        "Spirit", "Toon", "Union", "Fusion", "Synchro", "Xyz", "Link"
    ];

    private static readonly string[] MonsterFrames =
    [
        "normal", "effect", "ritual", "fusion", "synchro", "xyz", "link",
        "normal_pendulum", "effect_pendulum", "ritual_pendulum", "fusion_pendulum",
        "synchro_pendulum", "xyz_pendulum"
    ];

    public static IReadOnlyList<CategoryBase> GetCategories(CardInfo info)
    {
        Dictionary<string, CategoryBase> result = new(StringComparer.Ordinal);
        HashSet<string> tokens = (info.Type ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        string? frame = info.FrameType?.Trim().ToLowerInvariant();
        bool monster = tokens.Contains("Monster") || MonsterFrames.Contains(frame);
        bool spell = string.Equals(info.Type, "Spell Card", StringComparison.OrdinalIgnoreCase) || frame == "spell";
        bool trap = string.Equals(info.Type, "Trap Card", StringComparison.OrdinalIgnoreCase) || frame == "trap";

        if (monster)
        {
            Add("kind:monster", "Monster");

            foreach (string facet in MonsterFacets.Where(tokens.Contains))
            {
                string family = facet is "Fusion" or "Synchro" or "Xyz" or "Link" or "Ritual"
                    ? "monster-type"
                    : "monster-trait";
                Add($"{family}:{facet.ToLowerInvariant()}", $"{facet} Monster");
            }

            Text("attribute", "Attribute", info.Attribute?.ToUpperInvariant());
            Text("monster-race", "Monster Type", info.Race);
            bool link = tokens.Contains("Link") || frame == "link";
            bool xyz = tokens.Contains("Xyz") || frame is "xyz" or "xyz_pendulum";

            if (link)
            {
                Number("link", "Link", info.LinkVal);
            }
            else if (xyz)
            {
                Number("rank", "Rank", info.Level);
            }
            else
            {
                Number("level", "Level", info.Level);
            }

            if (tokens.Contains("Pendulum") || frame?.EndsWith("_pendulum", StringComparison.Ordinal) == true)
            {
                Number("scale", "Pendulum Scale", info.Scale);
            }
        }
        else if (spell || trap)
        {
            string kind = spell ? "spell" : "trap";
            string label = spell ? "Spell" : "Trap";
            Add($"kind:{kind}", label);
            string[] subtypes = spell
                ? ["Normal", "Field", "Equip", "Continuous", "Quick-Play", "Ritual"]
                : ["Normal", "Continuous", "Counter"];
            string? subtype = subtypes.FirstOrDefault(value =>
                string.Equals(value, info.Race?.Trim(), StringComparison.OrdinalIgnoreCase));

            if (subtype is not null)
            {
                Add($"{kind}-type:{subtype.ToLowerInvariant()}", $"{subtype} {label}");
            }
        }

        Text("archetype", "Archetype", info.Archetype);

        return result.Values.ToArray();

        void Add(string key, string label) => result.TryAdd(key, new(label, CategorySource.Metadata, key));

        void Number(string key, string label, int? value)
        {
            if (value is >= 0)
            {
                string number = value.Value.ToString(CultureInfo.InvariantCulture);
                Add($"{key}:{number}", $"{label} {number}");
            }
        }

        void Text(string key, string label, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            string trimmed = value.Trim();
            // Escaping retains punctuation and whitespace distinctions rather than creating slug collisions.
            Add($"{key}:{Uri.EscapeDataString(trimmed.ToLowerInvariant())}", $"{label}: {trimmed}");
        }
    }
}
