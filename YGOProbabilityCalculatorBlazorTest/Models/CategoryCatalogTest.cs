using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.DeckImport;

namespace YGOProbabilityCalculatorBlazorTest.Models;

public class CategoryCatalogTest {
    [Test]
    public void EmptyCatalogContainsBoundedCommonPropertiesInStableOrder() {
        CategoryCatalogOptions options = CategoryCatalog.BuildOptions([], [], []);
        string[] identities = options.All
            .Where(static category => category.Source == CategorySource.Metadata)
            .Select(static category => category.Identity)
            .ToArray();

        Assert.Multiple(() => {
            Assert.That(identities, Has.Length.EqualTo(CardPropertyProvider.CommonCategories.Count));
            Assert.That(identities.Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(identities.Length));
            Assert.That(options.All.Select(static category => category.Name), Does.Contain("Monster").And.Contain("Spell").And.Contain("Trap"));
            Assert.That(options.All.Select(static category => category.Name), Does.Contain("Attribute: DARK").And.Contain("Attribute: DIVINE"));
            Assert.That(options.All.Select(static category => category.Name), Does.Contain("Normal Monster").And.Contain("Effect Monster")
                .And.Contain("Tuner Monster").And.Contain("Pendulum Monster").And.Contain("Fusion Monster")
                .And.Contain("Synchro Monster").And.Contain("Xyz Monster").And.Contain("Link Monster").And.Contain("Ritual Monster"));
            Assert.That(options.All.Select(static category => category.Name), Does.Contain("Quick-Play Spell").And.Contain("Counter Trap")
                .And.Contain("Continuous Spell").And.Contain("Continuous Trap"));
        });

        Assert.That(CommonKeys("level:"), Is.EqualTo(Enumerable.Range(1, 12).Select(static value => $"level:{value}")));
        Assert.That(CommonKeys("rank:"), Is.EqualTo(Enumerable.Range(0, 14).Select(static value => $"rank:{value}")));
        Assert.That(CommonKeys("link:"), Is.EqualTo(Enumerable.Range(1, 6).Select(static value => $"link:{value}")));
        Assert.That(identities.Any(static identity => identity.StartsWith("metadata:archetype:", StringComparison.Ordinal)), Is.False);
        Assert.That(identities.Any(static identity => identity.StartsWith("metadata:monster-race:", StringComparison.Ordinal)), Is.False);
        Assert.That(identities.Any(static identity => identity.StartsWith("metadata:scale:", StringComparison.Ordinal)), Is.False);
        Assert.That(identities, Is.EqualTo(CardPropertyProvider.CommonCategories.Select(static category => category.Identity)));
    }

    [Test]
    public void CommonIdentitiesMatchProviderOutputForEveryRepresentativeFamily() {
        CardInfo[] examples = [
            new() { Type = "Monster" },
            new() { Type = "Effect Monster", Attribute = "DARK" },
            new() { Type = "Synchro Monster", FrameType = "synchro", Level = 1 },
            new() { Type = "Effect Monster", Level = 8 },
            new() { Type = "XYZ Monster", FrameType = "xyz", Level = 4 },
            new() { Type = "Link Monster", FrameType = "link", LinkVal = 2 },
            new() { Type = "Spell Card", Race = "Quick-Play" }
        ];
        string[] expectedKeys = [
            "kind:monster", "attribute:dark", "level:1", "level:8", "rank:4", "link:2", "kind:spell", "spell-type:quick-play"
        ];
        HashSet<string> commonIdentities = CardPropertyProvider.CommonCategories
            .Select(static category => category.Identity)
            .ToHashSet(StringComparer.Ordinal);
        HashSet<string> providerIdentities = examples
            .SelectMany(CardPropertyProvider.GetCategories)
            .Where(category => expectedKeys.Contains(category.MetadataKey, StringComparer.Ordinal))
            .Select(static category => category.Identity)
            .ToHashSet(StringComparer.Ordinal);

        Assert.That(providerIdentities, Is.EquivalentTo(expectedKeys.Select(key => $"metadata:{key}")));
        Assert.That(providerIdentities.IsSubsetOf(commonIdentities), Is.True);
        Assert.That(CardPropertyProvider.GetCategories(new() { Type = "Synchro Monster", Level = 0 })
            .Select(static category => category.MetadataKey), Does.Not.Contain("level:0"));
    }

    [Test]
    public void CommonMetadataIsDeduplicatedAcrossCardsCombosAndEnrichedCatalogInputs() {
        CategoryBase dark = CardPropertyProvider.GetCategories(new() { Type = "Effect Monster", Attribute = "DARK" })
            .Single(static category => category.MetadataKey == "attribute:dark");
        CategoryBase sameLabelUserCategory = new("DARK");
        Card card = new([dark]);
        Combo combo = new([new(dark, 1, 5)]);
        CategoryCatalogOptions options = CategoryCatalog.BuildOptions([sameLabelUserCategory], [card], [combo]);
        IReadOnlyList<CategoryBase> rebuilt = CategoryCatalog.Build(options.All, [], []);

        Assert.Multiple(() => {
            Assert.That(options.RelevantMetadata.Count(category => category.Identity == dark.Identity), Is.EqualTo(1));
            Assert.That(options.RemainingCommonMetadata.Any(category => category.Identity == dark.Identity), Is.False);
            Assert.That(options.All.Count(category => category.Identity == dark.Identity), Is.EqualTo(1));
            Assert.That(options.All.Count(category => category.Name == "DARK"), Is.EqualTo(1));
            Assert.That(options.All.Single(category => category.Name == "DARK").Identity, Is.EqualTo(sameLabelUserCategory.Identity));
            Assert.That(rebuilt.Select(static category => category.Identity), Is.EqualTo(options.All.Select(static category => category.Identity)));
        });
    }

    [Test]
    public void CommonPropertyAlreadyUsedOnlyByDrawSelectorIsPrioritizedAsRelevant() {
        CategoryBase dark = CardPropertyProvider.CommonCategories.Single(static category => category.MetadataKey == "attribute:dark");
        Card card = new([], drawCount: 1, preDrawRemovalCount: 1, preDrawSelector: CardSelector.For(dark));

        CategoryCatalogOptions options = CategoryCatalog.BuildOptions([], [card], []);

        Assert.That(options.RelevantMetadata.Select(static category => category.Identity), Does.Contain(dark.Identity));
        Assert.That(options.RemainingCommonMetadata.Select(static category => category.Identity), Does.Not.Contain(dark.Identity));
    }

    [Test]
    public void HighCardinalityMetadataStaysContextualAndComboReferencesRetainIt() {
        CategoryBase archetype = CardPropertyProvider.GetCategories(new() { Type = "Effect Monster", Archetype = "Orcust" })
            .Single(static category => category.MetadataKey == "archetype:orcust");
        CategoryBase race = CardPropertyProvider.GetCategories(new() { Type = "Effect Monster", Race = "Warrior" })
            .Single(static category => category.MetadataKey == "monster-race:warrior");
        CategoryBase scale = CardPropertyProvider.GetCategories(new() { Type = "Pendulum Effect Monster", Scale = 8 })
            .Single(static category => category.MetadataKey == "scale:8");

        Assert.That(CategoryCatalog.Build([], [], []).Select(static category => category.Identity), Does.Not.Contain(archetype.Identity));

        CategoryCatalogOptions cardOptions = CategoryCatalog.BuildOptions([], [new([archetype, race, scale])], []);
        CategoryCatalogOptions comboOptions = CategoryCatalog.BuildOptions([], [], [new([new(archetype, 1, 5)])]);

        Assert.Multiple(() => {
            Assert.That(cardOptions.RelevantMetadata.Select(static category => category.Identity),
                Does.Contain(archetype.Identity).And.Contain(race.Identity).And.Contain(scale.Identity));
            Assert.That(comboOptions.RelevantMetadata.Select(static category => category.Identity), Does.Contain(archetype.Identity));
            Assert.That(CategoryCatalog.Build([], [], []).Select(static category => category.Identity), Does.Not.Contain(race.Identity));
            Assert.That(CategoryCatalog.Build([], [], []).Select(static category => category.Identity), Does.Not.Contain(scale.Identity));
        });
    }

    private static string[] CommonKeys(string prefix) => CardPropertyProvider.CommonCategories
        .Where(category => category.MetadataKey!.StartsWith(prefix, StringComparison.Ordinal))
        .Select(static category => category.MetadataKey!)
        .ToArray();
}
