using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazorTest.Models;

public class CategoryIdentityTest {
    [Test]
    public void IdentitySeparatesSourcesAndIgnoresMetadataDisplayLabels() {
        var user = new CategoryBase("Spell");
        var property = new CategoryBase("Spell", CategorySource.Metadata, "kind:spell");
        var relabeled = new CategoryBase("Spells", CategorySource.Metadata, "kind:spell");
        Assert.That(user.Source, Is.EqualTo(CategorySource.User));
        Assert.That(user.MetadataKey, Is.Null);
        Assert.That(user, Is.Not.EqualTo(property));
        Assert.That(property, Is.EqualTo(relabeled));
        Assert.That(property.GetHashCode(), Is.EqualTo(relabeled.GetHashCode()));
        Assert.That(new[] { user, property, relabeled }.Distinct().Count(), Is.EqualTo(2));
        Assert.That(new CategoryBase("Spell"), Is.EqualTo(user));
        Assert.That(new CategoryBase("spell"), Is.Not.EqualTo(user));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" ")]
    public void MetadataRequiresAKey(string? key) =>
        Assert.Throws<ArgumentException>(() => new CategoryBase("Spell", CategorySource.Metadata, key));

    [Test]
    public void CopyMethodsPreserveExternalAndInternalCardIdentity() {
        var card = new Card([], 1, "Spell", externalCardId: 123);
        var updated = card.WithName("Renamed").WithCopies(3).WithCategories([new("Role")]).WithActive(false);
        Assert.That(updated.ExternalCardId, Is.EqualTo(123));
        Assert.That(updated.Id, Is.EqualTo(card.Id));
        Assert.That(new Card([]).ExternalCardId, Is.Null);
    }
}
