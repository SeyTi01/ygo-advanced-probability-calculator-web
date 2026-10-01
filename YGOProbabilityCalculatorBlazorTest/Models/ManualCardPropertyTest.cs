using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazorTest.Models;

public class ManualCardPropertyTest {
    private static readonly CategoryBase Fire = new("Attribute: FIRE", CategorySource.Metadata, "attribute:fire");
    private static readonly CategoryBase Spell = new("Spell", CategorySource.Metadata, "kind:spell");

    [Test]
    public void CopyOperationsPreserveProvenanceAndCategoryEditsDropOnlyRemovedKeys() {
        var user = new CategoryBase("Attribute: FIRE");
        var card = new Card([Spell, user], 2, "ROTA", false, "rota", 123).WithManualMetadataCategory(Fire);
        foreach (var changed in new[] { card.WithName("Renamed"), card.WithCopies(3), card.WithActive(true),
                     card.WithCategories(card.Categories.Append(new CategoryBase("Role"))) }) {
            Assert.That(changed.ManualMetadataCategoryKeys, Is.EquivalentTo(new[] { Fire.MetadataKey }));
            Assert.That(changed.Categories, Does.Contain(Fire).And.Contain(Spell));
            Assert.That((changed.Id, changed.ExternalCardId), Is.EqualTo((card.Id, card.ExternalCardId)));
        }
        var removedUser = card.WithCategories(card.Categories.Where(c => c != user));
        Assert.That(removedUser.ManualMetadataCategoryKeys, Is.EquivalentTo(card.ManualMetadataCategoryKeys));
        var removedFire = card.WithCategories(card.Categories.Where(c => c != Fire));
        Assert.That(removedFire.ManualMetadataCategoryKeys, Is.Empty);
        Assert.That(card.ManualMetadataCategoryKeys, Has.Count.EqualTo(1));
    }

    [Test]
    public void ManualAddAndRemoveCannotDuplicateOrRemoveObjectiveProperties() {
        var objective = new Card([Fire]);
        Assert.That(objective.WithManualMetadataCategory(Fire), Is.SameAs(objective));
        Assert.That(objective.WithoutManualMetadataCategory(Fire.MetadataKey!), Is.SameAs(objective));
        var manual = new Card([Spell]).WithManualMetadataCategory(Fire).WithManualMetadataCategory(Fire);
        Assert.That(manual.Categories, Is.EqualTo(new[] { Spell, Fire }));
        Assert.That(manual.ManualMetadataCategoryKeys, Has.Count.EqualTo(1));
        var removed = manual.WithoutManualMetadataCategory(Fire.MetadataKey!);
        Assert.That(removed.Categories, Is.EqualTo(new[] { Spell }));
        Assert.That(removed.ManualMetadataCategoryKeys, Is.Empty);
        Assert.Throws<ArgumentException>(() => new Card([Fire], manualMetadataCategoryKeys: ["attribute:water"]));
        Assert.Throws<ArgumentException>(() => manual.WithManualMetadataCategory(new("Role")));
        Assert.Throws<ArgumentException>(() => manual.WithObjectiveMetadata([new("Role")], 123));
    }
}
