using Microsoft.JSInterop;
using Moq;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;
using YGOProbabilityCalculatorBlazor.Services.Session;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

internal static class RealisticAllureFixture {
    internal static async Task<SessionState> Load(int copies) {
        SessionService codec = new(Mock.Of<IJSRuntime>(), new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());
        SessionState session = await codec.LoadSessionAsync(File.ReadAllText(Path.Combine(
            TestContext.CurrentContext.TestDirectory, "Fixtures", "example_session_state.json")));
        Card original = session.Cards.Single(card => card.Name == "Mulcharmy Fuwalos");
        Assert.That(session.Combos.SelectMany(combo => combo.AllCards).Any(card => card.CardId == original.Id), Is.False);
        MaintainedCardEffect effect = CardEffectRegistry.Default.Find(1475311)!;
        Card allure = new Card([new("Spell", CategorySource.Metadata, "kind:spell"),
            new("Normal Spell", CategorySource.Metadata, "spell-type:normal")], copies, effect.Name,
            id: "benchmark-allure", externalCardId: effect.CanonicalCardId).WithEffect(effect.Plan, effect.Profile);
        int index = session.Cards.IndexOf(original);
        session.Cards[index] = allure;
        if (copies < original.Copies) {
            session.Cards.Insert(index + 1, original.WithCopies(original.Copies - copies));
        }
        List<Card> materialized = CategoryMembership.Materialize(session.Cards, session.Categories).ToList();
        session.Cards.Clear();
        session.Cards.AddRange(materialized);
        Assert.Multiple(() => {
            Assert.That(session.Cards.Sum(card => card.Copies), Is.EqualTo(40));
            Assert.That(session.HandSize, Is.EqualTo(5));
            Assert.That(session.Combos, Has.Count.EqualTo(8));
            Assert.That(session.ComboGroups, Has.Count.EqualTo(2));
        });
        return session;
    }
}
