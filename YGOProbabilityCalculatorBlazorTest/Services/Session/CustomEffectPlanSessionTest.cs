using System.Text.Json;
using Microsoft.JSInterop;
using Moq;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;
using YGOProbabilityCalculatorBlazor.Services.Session;

namespace YGOProbabilityCalculatorBlazorTest.Services.Session;

public class CustomEffectPlanSessionTest {
    private static IEnumerable<TestCaseData> SavedPreferences() => Enumerable.Range(0, 6)
        .SelectMany(schema => new[] { true, false }.Select(enabled => new TestCaseData(schema, enabled)));

    [TestCaseSource(nameof(SavedPreferences))]
    public async Task SchemaZeroThroughFiveKeepExistingPreferenceMigration(int schema, bool enabled) {
        SessionService codec = new(Mock.Of<IJSRuntime>(), new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());
        string json = $$"""{"SchemaVersion":{{schema}},"DrawEffectsEnabled":{{enabled.ToString().ToLowerInvariant()}},"Cards":[{"Id":"saved","Name":"Trade-In","ExternalCardId":38120068,"Categories":[],"Copies":1,"DrawCount":2,"PreDrawRemovalCount":0}]}""";
        SessionState loaded = await codec.LoadSessionAsync(json);
        // Draw preferences and effects first existed in v4; retain older migrations.
        Assert.That(loaded.DrawEffectsEnabled, Is.EqualTo(schema < 4 || enabled));
        Assert.That(loaded.Cards[0].DrawCount, Is.EqualTo(schema < 4 ? null : (int?)2));
        Assert.That(loaded.Cards[0].PreDrawRemovalCount, Is.Zero);
        Assert.That(loaded.SchemaVersion, Is.EqualTo(SessionState.CurrentSchemaVersion));
    }

    [TestCase(true), TestCase(false)]
    public async Task CustomZeroCountsAndRetainedOperationsSurviveJsonSharingAndRestore(bool enabled) {
        SessionService codec = new(Mock.Of<IJSRuntime>(), new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());
        CategoryBase role = new("Role");
        List<Card> builtIns = new[] { 38120068, 1475311, 35261759, 49238328, 691925 }.Select((id, index) =>
            CardEffectRegistry.Default.RestoreBuiltIn(new([role], name: "Same name", id: $"entry-{index}", externalCardId: id))).ToList();
        List<Card> custom = [builtIns[0].WithPreDrawRemovalCount(0), builtIns[1].WithPostDrawDiscardCount(0),
            builtIns[2].WithPreDrawDeckRemovalCount(0), builtIns[3].WithPreDrawDeckRemovalCount(0), builtIns[4].WithPreDrawRemovalCount(0)];
        // A zero-valued edited operation must not erase other nonzero operations.
        custom[0] = custom[0].WithPostDrawDiscardCount(1).WithPostDrawDeckMillCount(1);
        SessionState session = new() {
            Cards = custom, Categories = [role], HandSize = 2, DrawEffectsEnabled = enabled,
            DrawActivationPriority = new(DrawActivationMode.CustomPriority, [custom[1].Id, custom[0].Id, custom[2].Id, custom[3].Id, custom[4].Id]),
            ComboGroups = [new("g", "Routes")], Combos = [new([new(role, 1, 2)], groupId: "g")]
        };
        foreach (bool restore in new[] { false, true }) {
            if (restore) {
                List<Card> restored = session.Cards.Select(CardEffectRegistry.Default.RestoreBuiltIn).ToList();
                session.Cards.Clear();
                session.Cards.AddRange(restored);
            }
            string json = codec.SerializeSession(session);
            Assert.That(json, Does.Not.Contain("ConditionalEffectsEnabled"));
            string fragment = new Uri(SessionShareCodec.CreateLink("https://example.invalid/", json)).Fragment;
            foreach (string source in new[] { json, SessionShareCodec.Decode(fragment) }) {
                SessionState loaded = await codec.LoadSessionAsync(source);
                Assert.That(loaded.SchemaVersion, Is.EqualTo(SessionState.CurrentSchemaVersion));
                Assert.That(loaded.DrawEffectsEnabled, Is.EqualTo(enabled));
                Assert.That(loaded.Cards.Select(card => (card.Id, card.EffectPlan, card.EffectProfile)),
                    Is.EqualTo(session.Cards.Select(card => (card.Id, card.EffectPlan, card.EffectProfile))));
                Assert.That(loaded.DrawActivationPriority.CardIds, Is.EqualTo(session.DrawActivationPriority.CardIds));
                Assert.That(loaded.Combos.Single().GroupId, Is.EqualTo("g"));
                Assert.That(loaded.Cards.All(card => card.Categories.Contains(role)), Is.True);
                CalculationSnapshot snapshot = CalculationSnapshot.Capture(loaded.Cards, loaded.Combos, 2, loaded.ComboGroups);
                CalculationInput input = JsonSerializer.Deserialize(snapshot.Json, CalculationJsonContext.Default.CalculationInput)!;
                Assert.That(input.Cards.Select(card => (card.PreDrawRemovalCount, card.PostDrawDiscardCount, card.PreDrawDeckRemovalCount,
                    card.PostDrawDeckMillCount, card.FirstActivationOnly, card.BlocksLaterDraws, card.EffectProfile)),
                    Is.EqualTo(session.Cards.Select(card => (card.PreDrawRemovalCount, card.PostDrawDiscardCount, card.PreDrawDeckRemovalCount,
                        card.PostDrawDeckMillCount, card.FirstActivationOnly, card.BlocksLaterDraws, card.EffectProfile))));
            }
        }
    }
}
