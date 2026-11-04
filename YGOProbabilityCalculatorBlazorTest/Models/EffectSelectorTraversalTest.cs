using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazorTest.Models;

public sealed class EffectSelectorTraversalTest {
    [Test]
    public void TraversalAndRenameIncludeCostsRemovalAndModesHiddenByAnyUnion() {
        CategoryBase cost = new("Cost"), removal = new("Removal"), target = new("Target");
        SearchEffectMode[] modes = [new(CardSelector.Any, 1), new(CardSelector.For(target), 2)];
        DrawEffectPlan original = DrawEffectPlan.SearchCards(new(CardSelector.Any, false, modes), false) with {
            HandCost = 1,
            HandCostSelector = CardSelector.For(cost),
            RemovalCount = 1,
            RemovalSelector = CardSelector.For(removal)
        };

        Assert.That(original.EnumerateCategoryIdentities(), Is.EquivalentTo(new[] { cost.Identity, removal.Identity, target.Identity }));
        DrawEffectPlan renamed = original.RenameCategory(cost.Identity, "user:Renamed cost")
            .RenameCategory(removal.Identity, "user:Renamed removal")
            .RenameCategory(target.Identity, "user:Renamed target");

        Assert.That(renamed.HandCostSelector.CategoryIdentities, Is.EqualTo(new[] { "user:Renamed cost" }));
        Assert.That(renamed.RemovalSelector.CategoryIdentities, Is.EqualTo(new[] { "user:Renamed removal" }));
        Assert.That(renamed.Search!.Selector, Is.EqualTo(CardSelector.Any));
        Assert.That(renamed.Search.Modes![0], Is.EqualTo(modes[0]));
        Assert.That(renamed.Search.Modes[1].Selector.CategoryIdentities, Is.EqualTo(new[] { "user:Renamed target" }));
        Assert.That(renamed.Search.Modes[1].ActivationLimit, Is.EqualTo(2));
        Assert.That(original.Search!.Modes![1], Is.EqualTo(modes[1]));
        Assert.DoesNotThrow(renamed.Search.Validate);
    }

    [Test]
    public void TransformRebuildsAggregateFromEachModeAndPreservesSearchPolicy() {
        CategoryBase first = new("First"), second = new("Second"), renamed = new("Renamed");
        SearchEffectPlan original = new(CardSelector.Any, true, [new(CardSelector.Any, 1), new(CardSelector.For(second), 2)]);

        SearchEffectPlan transformed = original.TransformSelectors(selector => selector.Kind == CardSelectorKind.Any
            ? CardSelector.For(first)
            : selector.RenameCategory(second.Identity, renamed.Identity));

        Assert.That(transformed.Selector, Is.EqualTo(CardSelector.AnyOfSelectors([CardSelector.For(first), CardSelector.For(renamed)])));
        Assert.That(transformed.Modes!.Select(mode => mode.ActivationLimit), Is.EqualTo(new int?[] { 1, 2 }));
        Assert.That(transformed.ObjectiveMetadataOnly, Is.True);
        Assert.DoesNotThrow(transformed.Validate);
        SearchEffectPlan single = new(CardSelector.For(second), false);
        Assert.That(single.RenameCategory(second.Identity, renamed.Identity), Is.EqualTo(new SearchEffectPlan(CardSelector.For(renamed), false)));
    }

    [Test]
    public void RemovingAReferencedModeMakesEffectCustomButMembershipOnlyRemovalKeepsProvenance() {
        CategoryBase role = new("Role");
        EffectProfile profile = new(EffectSource.BuiltIn, 78058681, 1, "Saved search", EffectSupport.Partial);
        Card modeSource = new([], search: new(CardSelector.Any, false, [new(CardSelector.Any, 1), new(CardSelector.For(role), 2)]), effectProfile: profile);

        Assert.That(CategoryRemoval.IsUsed(role, [modeSource], []), Is.True);
        Card removed = CategoryRemoval.Remove(modeSource, role.Identity);

        Assert.That(removed.EffectProfile, Is.EqualTo(profile.AsCustom()));
        Assert.That(removed.Search!.Selector, Is.EqualTo(CardSelector.Any));
        Assert.That(removed.Search.Modes![0], Is.EqualTo(modeSource.Search!.Modes![0]));
        Assert.That(removed.Search.Modes[1], Is.EqualTo(new SearchEffectMode(CardSelector.Not(CardSelector.Any), 2)));
        Assert.DoesNotThrow(removed.Search.Validate);
        Card membershipOnly = new([role], search: new(CardSelector.Any), effectProfile: profile);
        Assert.That(CategoryRemoval.Remove(membershipOnly, role.Identity).EffectProfile, Is.SameAs(profile));
    }
}
