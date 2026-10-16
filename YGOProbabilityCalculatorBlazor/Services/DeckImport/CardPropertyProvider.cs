using System.Globalization;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.DeckImport;

public static class CardPropertyProvider {
    // Tokens correspond to the documented API type vocabulary, including combined types.
    private static readonly string[] MonsterFacets = [
        "Normal", "Effect", "Flip", "Tuner", "Gemini", "Pendulum", "Ritual",
        "Spirit", "Toon", "Union", "Fusion", "Synchro", "Xyz", "Link"
    ];
    private static readonly string[] MonsterFrames = [
        "normal", "effect", "ritual", "fusion", "synchro", "xyz", "link",
        "normal_pendulum", "effect_pendulum", "ritual_pendulum", "fusion_pendulum",
        "synchro_pendulum", "xyz_pendulum"
    ];

    public static IReadOnlyList<CategoryBase> CommonCategories { get; } = Array.AsReadOnly(CreateCommonCategories());

    private static CategoryBase[] CreateCommonCategories() {
        List<CardInfo> examples = [
            new() { Type = "Monster" },
            new() { Type = "Spell Card" },
            new() { Type = "Trap Card" },
            new() { Type = "Normal Monster" },
            new() { Type = "Effect Monster" },
            new() { Type = "Tuner Monster" },
            new() { Type = "Pendulum Effect Monster" },
            new() { Type = "Ritual Monster" },
            new() { Type = "Fusion Monster" },
            new() { Type = "Synchro Monster" },
            new() { Type = "XYZ Monster" },
            new() { Type = "Link Monster" },
            new() { Type = "Flip Effect Monster" },
            new() { Type = "Gemini Monster" },
            new() { Type = "Spirit Monster" },
            new() { Type = "Toon Monster" },
            new() { Type = "Union Effect Monster" }
        ];

        foreach (string attribute in new[] { "DARK", "LIGHT", "EARTH", "WATER", "FIRE", "WIND", "DIVINE" }) {
            examples.Add(new() { Type = "Monster", Attribute = attribute });
        }

        for (int level = 1; level <= 12; level++) {
            examples.Add(new() { Type = "Monster", Level = level });
        }

        for (int rank = 0; rank <= 13; rank++) {
            examples.Add(new() { Type = "XYZ Monster", FrameType = "xyz", Level = rank });
        }

        for (int link = 1; link <= 6; link++) {
            examples.Add(new() { Type = "Link Monster", FrameType = "link", LinkVal = link });
        }

        foreach (string subtype in new[] { "Normal", "Field", "Equip", "Continuous", "Quick-Play", "Ritual" }) {
            examples.Add(new() { Type = "Spell Card", Race = subtype });
        }

        foreach (string subtype in new[] { "Normal", "Continuous", "Counter" }) {
            examples.Add(new() { Type = "Trap Card", Race = subtype });
        }

        return examples
            .SelectMany(GetCategories)
            .DistinctBy(static category => category.Identity)
            .ToArray();
    }

    public static IReadOnlyList<CategoryBase> GetCategories(CardInfo info) {
        Dictionary<string, CategoryBase> result = new(StringComparer.Ordinal);
        HashSet<string> tokens = (info.Type ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string? frame = info.FrameType?.Trim().ToLowerInvariant();
        bool monster = tokens.Contains("Monster") || MonsterFrames.Contains(frame);
        bool spell = string.Equals(info.Type, "Spell Card", StringComparison.OrdinalIgnoreCase) || frame == "spell";
        bool trap = string.Equals(info.Type, "Trap Card", StringComparison.OrdinalIgnoreCase) || frame == "trap";

        if (monster) {
            Add("kind:monster", "Monster");

            foreach (string facet in MonsterFacets.Where(tokens.Contains)) {
                string family = facet is "Fusion" or "Synchro" or "Xyz" or "Link" or "Ritual"
                    ? "monster-type"
                    : "monster-trait";

                Add($"{family}:{facet.ToLowerInvariant()}", $"{facet} Monster");
            }

            Text("attribute", "Attribute", info.Attribute?.ToUpperInvariant());
            Text("monster-race", "Monster Type", info.Race);
            bool isLinkMonster = tokens.Contains("Link") || frame == "link";
            bool isXyzMonster = tokens.Contains("Xyz") || frame is "xyz" or "xyz_pendulum";

            if (isLinkMonster) {
                Number("link", "Link", info.LinkVal);
            }
            else if (isXyzMonster) {
                Number("rank", "Rank", info.Level);
            }
            else {
                Number("level", "Level", info.Level);
            }

            bool hasPendulumType = tokens.Contains("Pendulum") || frame?.EndsWith("_pendulum", StringComparison.Ordinal) == true;

            if (hasPendulumType) {
                Number("scale", "Pendulum Scale", info.Scale);
            }
        }
        else if (spell || trap) {
            string kind = spell ? "spell" : "trap";
            string label = spell ? "Spell" : "Trap";
            Add($"kind:{kind}", label);

            string[] subtypes = spell
                ? ["Normal", "Field", "Equip", "Continuous", "Quick-Play", "Ritual"]
                : ["Normal", "Continuous", "Counter"];

            string? subtype = subtypes.FirstOrDefault(value => string.Equals(value, info.Race?.Trim(), StringComparison.OrdinalIgnoreCase));

            if (subtype is not null) {
                Add($"{kind}-type:{subtype.ToLowerInvariant()}", $"{subtype} {label}");
            }
        }

        foreach (string archetype in info.ResolvedArchetypes ?? []) {
            Text("archetype", "Archetype", archetype);
        }

        if (info.ResolvedNames is not null && (info.CanonicalCardId ?? info.Id) is > 0 and var canonicalId) {
            Add($"catalog-id:{canonicalId.ToString(CultureInfo.InvariantCulture)}", $"Card identity: {info.Name}");
        }
        foreach (string name in info.ResolvedNames ?? []) {
            Text("card-name", "Card name", name);
        }
        foreach (string name in info.MentionedNames ?? []) {
            Text("mentions", "Mentions", name);
        }

        return result.Values.ToArray();

        void Add(string key, string label) => result.TryAdd(key, new(label, CategorySource.Metadata, key));

        void Number(string key, string label, int? value) {
            if (value is >= 0 && (key != "level" || value > 0)) {
                string number = value.Value.ToString(CultureInfo.InvariantCulture);
                Add($"{key}:{number}", $"{label} {number}");
            }
        }

        void Text(string key, string label, string? value) {
            if (string.IsNullOrWhiteSpace(value)) {
                return;
            }

            string trimmed = value.Trim();
            // Escaping retains punctuation and whitespace distinctions rather than creating slug collisions.
            Add($"{key}:{Uri.EscapeDataString(trimmed.ToLowerInvariant())}", $"{label}: {trimmed}");
        }
    }
}
