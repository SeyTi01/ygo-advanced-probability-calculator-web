using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.DeckImport;

namespace YGOProbabilityCalculatorBlazorTest.Services.DeckImport;

public class CardPropertyProviderTest
{
    // Full documented type list: explicit expectations guard combined-type handling.
    [TestCase("Effect Monster", "Effect")]
    [TestCase("Flip Effect Monster", "Flip,Effect")]
    [TestCase("Flip Tuner Effect Monster", "Flip,Tuner,Effect")]
    [TestCase("Gemini Monster", "Gemini")]
    [TestCase("Normal Monster", "Normal")]
    [TestCase("Normal Tuner Monster", "Normal,Tuner")]
    [TestCase("Pendulum Effect Monster", "Pendulum,Effect")]
    [TestCase("Pendulum Effect Ritual Monster", "Pendulum,Effect,Ritual")]
    [TestCase("Pendulum Flip Effect Monster", "Pendulum,Flip,Effect")]
    [TestCase("Pendulum Normal Monster", "Pendulum,Normal")]
    [TestCase("Pendulum Tuner Effect Monster", "Pendulum,Tuner,Effect")]
    [TestCase("Ritual Effect Monster", "Ritual,Effect")]
    [TestCase("Ritual Monster", "Ritual")]
    [TestCase("Spirit Monster", "Spirit")]
    [TestCase("Toon Monster", "Toon")]
    [TestCase("Tuner Monster", "Tuner")]
    [TestCase("Union Effect Monster", "Union,Effect")]
    [TestCase("Fusion Monster", "Fusion")]
    [TestCase("Link Monster", "Link")]
    [TestCase("Pendulum Effect Fusion Monster", "Pendulum,Effect,Fusion")]
    [TestCase("Synchro Monster", "Synchro")]
    [TestCase("Synchro Pendulum Effect Monster", "Synchro,Pendulum,Effect")]
    [TestCase("Synchro Tuner Monster", "Synchro,Tuner")]
    [TestCase("XYZ Monster", "Xyz")]
    [TestCase("XYZ Pendulum Effect Monster", "Xyz,Pendulum,Effect")]
    public void DocumentedMonsterTypesEmitOnlyStatedFacets(string type, string facets)
    {
        IReadOnlyList<CategoryBase> actual = CardPropertyProvider.GetCategories(
            new() { Type = type }
        );
        IEnumerable<string> expected = facets
            .Split(',')
            .Select(facet => $"{facet} Monster")
            .Prepend("Monster");
        Assert.That(actual.Select(category => category.Name), Is.EquivalentTo(expected));
        Assert.That(actual.All(category => category.Source == CategorySource.Metadata), Is.True);
        Assert.That(
            actual.Select(category => category.MetadataKey).Distinct().Count(),
            Is.EqualTo(actual.Count)
        );
    }

    [TestCase("Spell Card", "Normal", "Spell", "Normal Spell")]
    [TestCase("Spell Card", "Field", "Spell", "Field Spell")]
    [TestCase("Spell Card", "Equip", "Spell", "Equip Spell")]
    [TestCase("Spell Card", "Continuous", "Spell", "Continuous Spell")]
    [TestCase("Spell Card", "Quick-Play", "Spell", "Quick-Play Spell")]
    [TestCase("Spell Card", "Ritual", "Spell", "Ritual Spell")]
    [TestCase("Trap Card", "Normal", "Trap", "Normal Trap")]
    [TestCase("Trap Card", "Continuous", "Trap", "Continuous Trap")]
    [TestCase("Trap Card", "Counter", "Trap", "Counter Trap")]
    public void SpellTrapSubtypesUseRace(string type, string race, string broad, string specific) =>
        Assert.That(
            CardPropertyProvider
                .GetCategories(new() { Type = type, Race = race })
                .Select(c => c.Name),
            Is.EqualTo(new[] { broad, specific })
        );

    [TestCase("Skill Card")]
    [TestCase("Token")]
    [TestCase(null)]
    public void MissingOrUnsupportedPropertiesProduceNoInventedCategories(string? type) =>
        Assert.That(CardPropertyProvider.GetCategories(new() { Type = type }), Is.Empty);

    [Test]
    public void NumericAndTextFacetsHaveCanonicalKeysAndNoSubjectiveInference()
    {
        CardInfo info = new()
        {
            Type = "Pendulum Effect Monster",
            Race = "Warrior",
            Attribute = "FIRE",
            Level = 5,
            Scale = 0,
            Archetype = "Vanquish Soul",
        };
        IReadOnlyList<CategoryBase> categories = CardPropertyProvider.GetCategories(info);
        Assert.That(
            categories.Select(c => c.MetadataKey),
            Is.EquivalentTo(
                new[]
                {
                    "kind:monster",
                    "monster-trait:pendulum",
                    "monster-trait:effect",
                    "monster-race:warrior",
                    "attribute:fire",
                    "level:5",
                    "scale:0",
                    "archetype:vanquish%20soul",
                }
            )
        );
        Assert.That(
            categories.Select(c => c.Name),
            Does.Contain("Pendulum Scale 0").And.Contain("Archetype: Vanquish Soul")
        );
        Assert.That(
            categories.Select(c => c.MetadataKey),
            Is.EqualTo(
                CardPropertyProvider
                    .GetCategories(
                        info with
                        {
                            Type = "PENDULUM EFFECT EFFECT MONSTER",
                            Attribute = "fire",
                            Race = "warrior",
                            Archetype = "vanquish soul",
                        }
                    )
                    .Select(c => c.MetadataKey)
            )
        );
    }

    [TestCase("XYZ Pendulum Effect Monster", "xyz_pendulum", "Rank 4")]
    [TestCase("Link Monster", "link", "Link 2")]
    public void ExtraDeckValuesDoNotBecomeLevels(string type, string frame, string expected)
    {
        IReadOnlyList<CategoryBase> categories = CardPropertyProvider.GetCategories(
            new()
            {
                Type = type,
                FrameType = frame,
                Level = 4,
                LinkVal = 2,
                Scale = 8,
            }
        );
        Assert.That(categories.Select(c => c.Name), Does.Contain(expected));
        Assert.That(categories.Any(c => c.MetadataKey!.StartsWith("level:")), Is.False);

        if (frame == "xyz_pendulum")
        {
            Assert.That(categories.Select(c => c.Name), Does.Contain("Pendulum Scale 8"));
        }
        else
        {
            Assert.That(categories.Any(c => c.MetadataKey!.StartsWith("scale:")), Is.False);
        }
    }

    [TestCase("effect", "Monster")]
    [TestCase("spell", "Spell")]
    [TestCase("trap", "Trap")]
    public void FrameCanSupplyReliableBroadKindWhenTypeIsMissing(string frame, string label) =>
        Assert.That(
            CardPropertyProvider.GetCategories(new() { FrameType = frame }).Select(c => c.Name),
            Is.EqualTo(new[] { label })
        );
}
