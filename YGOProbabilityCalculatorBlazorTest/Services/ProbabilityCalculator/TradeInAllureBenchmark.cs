using System.Text.Json;
using Microsoft.JSInterop;
using Moq;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.Session;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

internal static class TradeInAllureFixture {
    internal static async Task<SessionState> Load() {
        SessionService codec = new(Mock.Of<IJSRuntime>(), new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());
        SessionState session = await codec.LoadSessionAsync(File.ReadAllText(Path.Combine(
            TestContext.CurrentContext.TestDirectory, "Fixtures", "example_session_state.json")));
        List<Card> effective = CategoryMembership.Materialize(session.Cards, session.Categories).ToList();
        string definitions = JsonSerializer.Serialize(session.Combos);
        string[] groupIds = session.ComboGroups.Select(group => group.Id).ToArray();
        Dictionary<string, int> substitutions = new() {
            ["Mulcharmy Fuwalos"] = 3, ["Maxx \"C\""] = 1,
            ["Triple Tactics Talent"] = 1, ["Vanquish Soul Snow Devil"] = 1
        };
        Card[] removed = effective.Where(card => substitutions.ContainsKey(card.Name!)).ToArray();
        Assert.That(removed, Has.Length.EqualTo(4));
        foreach (Card card in removed) {
            Assert.That(card.Copies, Is.EqualTo(substitutions[card.Name!]));
            Assert.That(session.Combos.Where(combo => combo.Active).Any(combo =>
                combo.AllCards.Any(requirement => requirement.CardId == card.Id)
                || combo.AllCategories.Any(requirement => requirement.Selector.Matches(card.Categories))), Is.False, card.Name);
        }
        List<Card> unchanged = effective.Except(removed).ToList();
        session.Cards.Clear();
        session.Cards.AddRange(unchanged);
        foreach ((int canonicalId, string entryId) in new[] { (38120068, "benchmark-trade-in"), (1475311, "benchmark-allure") }) {
            MaintainedCardEffect effect = CardEffectRegistry.Default.Find(canonicalId)!;
            session.Cards.Add(new Card([
                new("Spell", CategorySource.Metadata, "kind:spell"),
                new("Normal Spell", CategorySource.Metadata, "spell-type:normal")
            ], 3, effect.Name, id: entryId, externalCardId: canonicalId).WithEffect(effect.Plan, effect.Profile));
        }
        Card trade = session.Cards[^2];
        Card allure = session.Cards[^1];
        CategoryBase dark = new("Attribute: DARK", CategorySource.Metadata, "attribute:dark");
        Assert.Multiple(() => {
            Assert.That(session.Cards.Sum(card => card.Copies), Is.EqualTo(40));
            Assert.That(session.HandSize, Is.EqualTo(5));
            Assert.That(session.Combos, Has.Count.EqualTo(8));
            Assert.That(session.Combos.All(combo => combo.Active), Is.True);
            Assert.That(JsonSerializer.Serialize(session.Combos), Is.EqualTo(definitions));
            Assert.That(session.ComboGroups.Select(group => group.Id), Is.EqualTo(groupIds));
            Assert.That(groupIds, Is.EqualTo(new[] { "48c6559aefb544fb949e8aabef2d251d", "0523b13956664dc7b0a36d0ec6c54b01" }));
            Assert.That(session.Cards.Take(unchanged.Count), Is.EqualTo(unchanged));
            Assert.That(session.Cards.Select(card => card.Id).Distinct().Count(), Is.EqualTo(session.Cards.Count));
            Assert.That(trade.PreDrawRemovalCount, Is.EqualTo(1));
            Assert.That(allure.PostDrawDiscardCount, Is.EqualTo(1));
            foreach (Card source in new[] { trade, allure }) {
                MaintainedCardEffect effect = CardEffectRegistry.Default.Find(source.ExternalCardId!.Value)!;
                Assert.That(source.Copies, Is.EqualTo(3));
                Assert.That(source.EffectPlan, Is.EqualTo(effect.Plan));
                Assert.That(source.EffectProfile, Is.EqualTo(effect.Profile));
                Assert.That(source.Categories.Select(category => category.MetadataKey),
                    Is.EquivalentTo(new[] { "kind:spell", "spell-type:normal" }));
            }
            Assert.That(unchanged.Where(card => trade.PreDrawSelector.Matches(card.Categories)).Select(card => card.Name),
                Is.EquivalentTo(new[] { "Dogoran, the Mad Flame Kaiju", "Vanquish Soul Caesar Valius" }));
            Assert.That(unchanged.Where(card => allure.PostDrawSelector.Matches(card.Categories)).Sum(card => card.Copies), Is.GreaterThan(0));
            Assert.That(session.Cards.Where(card => card.Categories.Any(category => category.Identity == dark.Identity)).Sum(card => card.Copies),
                Is.EqualTo(effective.Where(card => card.Categories.Any(category => category.Identity == dark.Identity)).Sum(card => card.Copies)));
        });
        return session;
    }
}

[TestFixture, Explicit("Isolated bounded acceptance measurements"), NonParallelizable]
public class TradeInAllureBenchmark {
    [Test]
    public async Task Export() {
        SessionState session = await TradeInAllureFixture.Load();
        // Keep the original 50M comparison explicit and reproducible after the policy change.
        long work = long.TryParse(Environment.GetEnvironmentVariable("YGO_ACCEPTANCE_WORK"), out long value) ? value : 50_000_000;
        CalculationStorageTier tier = Enum.TryParse(Environment.GetEnvironmentVariable("YGO_ACCEPTANCE_TIER"), out CalculationStorageTier selected)
            ? selected : CalculationStorageTier.Standard;
        bool custom = Environment.GetEnvironmentVariable("YGO_ACCEPTANCE_CUSTOM") == "1";
        DrawActivationPriority priority = custom ? new(DrawActivationMode.CustomPriority, ["benchmark-allure", "benchmark-trade-in"])
            : DrawActivationPriority.Default;
        CalculationSnapshot snapshot = CalculationSnapshot.Capture(session.Cards, session.Combos, session.HandSize, session.ComboGroups,
            new(work), storagePolicy: CalculationStoragePolicy.ForTier(tier), options: new(true, priority));
        SessionService codec = new(Mock.Of<IJSRuntime>(), new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());
        SessionState exported = new() {
            Cards = session.Cards, Categories = session.Categories, Combos = session.Combos,
            ComboGroups = session.ComboGroups, HandSize = session.HandSize,
            CategoryColorIndices = session.CategoryColorIndices, DrawActivationPriority = priority
        };
        File.WriteAllText(Environment.GetEnvironmentVariable("YGO_AUDIT_INPUT")!, JsonSerializer.Serialize(new[] { new {
            Name = $"trade-in-allure-{tier}-{work}-{(custom ? "custom" : "encounter")}",
            Input = JsonSerializer.Deserialize<JsonElement>(snapshot.Json),
            Session = JsonSerializer.Deserialize<JsonElement>(codec.SerializeSession(exported))
        } }));
    }
}
