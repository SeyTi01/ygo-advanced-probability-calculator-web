using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.DeckImport;

namespace YGOProbabilityCalculatorBlazorTest.Services.DeckImport;

[TestFixture]
public class CuratedDrawEffectPresetsTest {
    [Test]
    public void PresetsHaveUniquePositiveIdsNamesAndSupportedDrawCounts() {
        IReadOnlyList<CuratedDrawEffectPreset> presets = CuratedDrawEffectPresets.All;

        Assert.Multiple(() => {
            Assert.That(presets, Has.Count.EqualTo(17));
            Assert.That(presets.All(preset => preset.CardId > 0), Is.True);
            Assert.That(presets.Select(preset => preset.CardId).Distinct().Count(), Is.EqualTo(presets.Count));
            Assert.That(presets.All(preset => !string.IsNullOrWhiteSpace(preset.Name)), Is.True);
            Assert.That(presets.All(preset => preset.DrawCount is >= 1 and <= 3), Is.True);
        });
    }

    [Test]
    public void PresetNamesAndCanonicalIdsMatchTheCheckedInCatalog() {
        string catalogPath = FindCatalogPath();
        using JsonDocument catalog = JsonDocument.Parse(File.ReadAllText(catalogPath));
        Dictionary<int, JsonElement> cardsById = catalog.RootElement.GetProperty("cards")
            .EnumerateArray()
            .ToDictionary(card => card.GetProperty("id").GetInt32());

        foreach (CuratedDrawEffectPreset preset in CuratedDrawEffectPresets.All) {
            Assert.That(cardsById.TryGetValue(preset.CardId, out JsonElement card), Is.True,
                $"Catalog does not contain card ID {preset.CardId} ({preset.Name}).");
            Assert.Multiple(() => {
                Assert.That(card.GetProperty("name").GetString(), Is.EqualTo(preset.Name));
                Assert.That(card.GetProperty("canonicalCardId").GetInt32(), Is.EqualTo(preset.CardId));
            });
        }
    }

    [Test]
    public void LookupUsesCanonicalIdentityThenFallsBackToCardIdAndNeverUsesName() {
        CuratedDrawEffectPreset potOfGreed = CuratedDrawEffectPresets.All.Single(preset => preset.Name == "Pot of Greed");

        Assert.Multiple(() => {
            Assert.That(CuratedDrawEffectPresets.Find(new CardInfo {
                Id = 999,
                CanonicalCardId = potOfGreed.CardId,
                Name = "Unrelated display name"
            }), Is.EqualTo(potOfGreed));
            Assert.That(CuratedDrawEffectPresets.Find(new CardInfo {
                Id = potOfGreed.CardId,
                Name = "Unrelated display name"
            }), Is.EqualTo(potOfGreed));
            Assert.That(CuratedDrawEffectPresets.Find(new CardInfo {
                Id = 999,
                Name = potOfGreed.Name
            }), Is.Null);
            Assert.That(CuratedDrawEffectPresets.Find(new CardInfo {
                Id = potOfGreed.CardId,
                CanonicalCardId = 999,
                Name = potOfGreed.Name
            }), Is.Null);
        });
    }

    [Test]
    public void ExplicitSelectionPreservesOverridesAcrossCanonicalAndArtworkAliases() {
        const int canonicalCardId = 55144522;
        const int selectedAliasId = 99999999;
        const int alternateArtworkId = 88888888;
        CardInfo selectedAlias = new() {
            Id = selectedAliasId,
            CanonicalCardId = canonicalCardId,
            Name = "Pot of Greed alternate artwork",
            ArtworkImageIds = [canonicalCardId, selectedAliasId, alternateArtworkId]
        };

        foreach (int previousExternalId in new[] { canonicalCardId, alternateArtworkId }) {
            Card previous = new([], name: "Pot of Greed", externalCardId: previousExternalId,
                drawCount: null, drawOncePerTurn: false);
            Card replacement = previous.WithObjectiveMetadata([], selectedAlias.Id).WithName(selectedAlias.Name);
            Card selected = CuratedDrawEffectPresets.ApplyForExplicitSelection(replacement, previous, selectedAlias);

            Assert.That((selected.ExternalCardId, selected.DrawCount, selected.DrawOncePerTurn),
                Is.EqualTo((selectedAliasId, (int?)null, false)));
        }
    }

    [Test]
    public void TradeInCostRequiresOnlyLevelEightForImportsAndSelections() {
        CardInfo info = new() { Id = 38120068, Name = "Trade-In" };
        Card imported = CuratedDrawEffectPresets.CreateImportedCard(info, 1, info.Id);
        Card selected = CuratedDrawEffectPresets.ApplyToSelectedCardIfPristine(new([]), info);
        CategoryBase levelEight = new("Level 8", CategorySource.Metadata, "level:8");
        CategoryBase monster = new("Monster", CategorySource.Metadata, "kind:monster");

        foreach (Card card in new[] { imported, selected }) {
            Assert.Multiple(() => {
                Assert.That(card.PreDrawSelector.Kind, Is.EqualTo(CardSelectorKind.Category));
                Assert.That(card.PreDrawSelector.Matches([levelEight]), Is.True);
                Assert.That(card.PreDrawSelector.Matches([monster]), Is.False);
                Assert.That(card.PreDrawRemovalCount, Is.EqualTo(1));
                Assert.That(card.DrawCount, Is.EqualTo(2));
                Assert.That(card.DrawOncePerTurn, Is.False);
            });
        }
    }

    private static string FindCatalogPath() {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null) {
            string path = Path.Combine(directory.FullName, "YGOProbabilityCalculatorBlazor", "wwwroot", "data", "card-catalog.v1.json");

            if (File.Exists(path)) {
                return path;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate the checked-in card catalog from the test output directory.");
    }

    [Test]
    public void PostDrawPresetsApplyOnlyToNewCanonicalSelectionsAndImports() {
        foreach (CuratedDrawEffectPreset preset in CuratedDrawEffectPresets.All) {
            int expected = preset.CardId switch { 74117290 or 1475311 or 20508881 => 1, 79571449 => 2, _ => 0 };
            CardInfo info = new() { Id = 999, CanonicalCardId = preset.CardId, Name = "Same" };
            Card imported = CuratedDrawEffectPresets.CreateImportedCard(info, 3, 999);
            Card selected = CuratedDrawEffectPresets.ApplyToSelectedCardIfPristine(new([], id: "stable"), info);
            Assert.That(imported.PostDrawDiscardCount, Is.EqualTo(expected));
            Assert.That(selected.PostDrawDiscardCount, Is.EqualTo(expected));
            Assert.That(imported.PostDrawSelector, Is.EqualTo(preset.PostDrawSelector ?? CardSelector.Any));
            Assert.That(selected.PostDrawSelector, Is.EqualTo(imported.PostDrawSelector));
            Assert.That(imported.PreDrawRemovalCount, Is.EqualTo(preset.PreDrawRemovalCount));
            Assert.That(selected.PreDrawSelector, Is.EqualTo(preset.PreDrawSelector ?? CardSelector.Any));
            Assert.That(selected.Id, Is.EqualTo("stable"));
            Card configured = selected.WithPostDrawDiscardCount(0);
            Assert.That(CuratedDrawEffectPresets.ApplyToSelectedCardIfPristine(configured, info).PostDrawDiscardCount, Is.Zero);
        }
    }
}
