using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

internal static class RealisticMixedFixture {
    internal static async Task<SessionState> Load(bool vision) {
        SessionState session = await RealisticAllureFixture.Load(2);
        int index = session.Cards.FindIndex(card => card.Name == "Mulcharmy Fuwalos");
        MaintainedCardEffect effect = CardEffectRegistry.Default.Find(vision ? 20508881 : 35261759)!;
        List<CategoryBase> categories = [new("Spell", CategorySource.Metadata, "kind:spell")];
        categories.Add(new(vision ? "Quick-Play Spell" : "Normal Spell", CategorySource.Metadata,
            vision ? "spell-type:quick-play" : "spell-type:normal"));
        if (vision) {
            categories.Add(new("Archetype: Radiant Typhoon", CategorySource.Metadata, "archetype:radiant%20typhoon"));
        }
        session.Cards[index] = new Card(categories, 1, effect.Name, id: "benchmark-secondary",
            externalCardId: effect.CanonicalCardId).WithEffect(effect.Plan, effect.Profile);
        Assert.Multiple(() => {
            Assert.That(session.Cards.Sum(card => card.Copies), Is.EqualTo(40));
            Assert.That(session.HandSize, Is.EqualTo(5));
            Assert.That(session.Combos, Has.Count.EqualTo(8));
            Assert.That(session.ComboGroups.Select(group => group.Id), Is.EqualTo(new[] {
                "48c6559aefb544fb949e8aabef2d251d", "0523b13956664dc7b0a36d0ec6c54b01"
            }));
            // Vision has legal Quick-Play material in the unchanged example.
            Assert.That(!vision || session.Cards.Any(card => card.Id != "benchmark-secondary"
                && effect.Plan.RemovalSelector.Matches(card.Categories)), Is.True);
        });
        return session;
    }
}
