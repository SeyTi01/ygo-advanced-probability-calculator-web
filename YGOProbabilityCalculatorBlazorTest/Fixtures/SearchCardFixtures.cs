using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;

namespace YGOProbabilityCalculatorBlazorTest.Fixtures;

internal static class SearchCardFixtures {
    internal static Card Warrior(int copies = 1, int level = 4, string race = "warrior", string id = "target") => new([
        new("A"), new("B"), new("Monster", CategorySource.Metadata, "kind:monster"),
        new("Warrior", CategorySource.Metadata, $"monster-race:{race}"),
        new($"Level {level}", CategorySource.Metadata, $"level:{level}")
    ], copies, id, id: id, externalCardId: 29302858);

    internal static Card Rota(int copies = 1) => CardEffectRegistry.Default.RestoreBuiltIn(
        new([new("Spell", CategorySource.Metadata, "kind:spell")], copies, "ROTA", id: "rota", externalCardId: 32807846));

    internal static Card Preset(int identity, int copies = 1, string? id = null) => CardEffectRegistry.Default.RestoreBuiltIn(
        new([new("Spell", CategorySource.Metadata, "kind:spell")], copies, id: id ?? $"source-{identity}", externalCardId: identity));
}
