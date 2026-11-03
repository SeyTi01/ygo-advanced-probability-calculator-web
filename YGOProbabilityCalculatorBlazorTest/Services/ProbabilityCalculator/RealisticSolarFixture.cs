using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

internal static class RealisticSolarFixture {
    internal static async Task<SessionState> Load(int copies) {
        SessionState session = await RealisticAllureFixture.Load(copies);
        MaintainedCardEffect solar = CardEffectRegistry.Default.Find(691925)!;
        int sourceIndex = session.Cards.FindIndex(card => card.Id == "benchmark-allure");
        session.Cards[sourceIndex] = new Card([
            new("Spell", CategorySource.Metadata, "kind:spell"),
            new("Normal Spell", CategorySource.Metadata, "spell-type:normal")
        ], copies, solar.Name, id: "benchmark-solar", externalCardId: solar.CanonicalCardId).WithEffect(solar.Plan, solar.Profile);

        // Maxx "C" and Ryko have no original combo role. Unlike replacing Ash,
        // this preserves every FIRE/DARK/Level 5 membership in the VS/K9 fixture.
        Card original = session.Cards.Single(card => card.Name == "Maxx \"C\"");
        Assert.That(original.Copies, Is.EqualTo(1));
        Assert.That(session.Combos.SelectMany(combo => combo.AllCards).Any(card => card.CardId == original.Id), Is.False);
        Card ryko = new([
            new("Monster", CategorySource.Metadata, "kind:monster"),
            new("Flip Monster", CategorySource.Metadata, "monster-trait:flip"),
            new("Effect Monster", CategorySource.Metadata, "monster-trait:effect"),
            new("Attribute: LIGHT", CategorySource.Metadata, "attribute:light"),
            new("Monster Type: Beast", CategorySource.Metadata, "monster-race:beast"),
            new("Level 2", CategorySource.Metadata, "level:2"),
            new("Archetype: Lightsworn", CategorySource.Metadata, "archetype:lightsworn")
        ], 1, "Ryko, Lightsworn Hunter", id: "benchmark-ryko", externalCardId: 21502796);
        Assert.That(session.Combos.SelectMany(combo => combo.AllCategories)
            .Any(requirement => requirement.Selector.Matches(original.Categories) || requirement.Selector.Matches(ryko.Categories)), Is.False);
        session.Cards[session.Cards.IndexOf(original)] = ryko;
        List<Card> materialized = CategoryMembership.Materialize(session.Cards, session.Categories).ToList();
        session.Cards.Clear();
        session.Cards.AddRange(materialized);
        Assert.Multiple(() => {
            Assert.That(session.Cards.Sum(card => card.Copies), Is.EqualTo(40));
            Assert.That(session.HandSize, Is.EqualTo(5));
            Assert.That(session.Combos, Has.Count.EqualTo(8));
            Assert.That(session.ComboGroups.Select(group => group.Id), Is.EqualTo(new[] {
                "48c6559aefb544fb949e8aabef2d251d", "0523b13956664dc7b0a36d0ec6c54b01"
            }));
            Assert.That(session.Cards.Where(card => solar.Plan.HandCostSelector.Matches(card.Categories)).Sum(card => card.Copies), Is.EqualTo(1));
        });

        return session;
    }
}
