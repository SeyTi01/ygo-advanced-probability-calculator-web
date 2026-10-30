using System.Text.Json;
using Microsoft.JSInterop;
using Moq;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.Session;

namespace YGOProbabilityCalculatorBlazorTest.Services.Session;

public class CustomEffectPlanSessionTest {
    [TestCase(43316238)]
    [TestCase(90807199)]
    [TestCase(26302107)]
    [TestCase(99745551)]
    [TestCase(16209941)]
    [TestCase(52350806)]
    [TestCase(70711847)]
    [TestCase(43694650)]
    [TestCase(83518674)]
    public async Task DangerOperationSurvivesSessionSharingAndWorkerWithoutBecomingOrdinaryDraw(int canonicalId) {
        SessionService codec = new(Mock.Of<IJSRuntime>(), new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());
        Card maintained = CardEffectRegistry.Default.RestoreBuiltIn(new([], name: "Renamed", id: "danger", externalCardId: canonicalId));
        foreach (bool enabled in new[] { true, false }) {
            Card source = maintained.WithSimulationEnabled(enabled);
            SessionState session = new() {
                Cards = [source, new([], 2, id: "filler")], HandSize = 1,
                Combos = [new([], "Any hand", groupId: "g")], ComboGroups = [new("g", "Group")]
            };
            string json = codec.SerializeSession(session);
            using JsonDocument document = JsonDocument.Parse(json);
            Assert.That(document.RootElement.GetProperty("SchemaVersion").GetInt32(), Is.EqualTo(14));
            JsonElement savedCard = document.RootElement.GetProperty("Cards")[0];
            Assert.That(savedCard.GetProperty("DrawOperation").GetString(), Is.EqualTo("DangerReveal"));
            Assert.That(savedCard.TryGetProperty("DrawCount", out _), Is.False);
            string fragment = new Uri(SessionShareCodec.CreateLink("https://example.invalid/", json)).Fragment;
            foreach (string payload in new[] { json, SessionShareCodec.Decode(fragment) }) {
                SessionState loaded = await codec.LoadSessionAsync(payload);
                Card restored = loaded.Cards[0];
                Assert.That((restored.EffectPlan, restored.EffectProfile, restored.SimulationEnabled, restored.Name),
                    Is.EqualTo((source.EffectPlan, source.EffectProfile, enabled, "Renamed")));
                Assert.That(codec.SerializeSession(loaded), Is.EqualTo(json));
                CalculationSnapshot snapshot = CalculationSnapshot.Capture(loaded.Cards, loaded.Combos, 1, loaded.ComboGroups);
                CalculationInput input = JsonSerializer.Deserialize(snapshot.Json, CalculationJsonContext.Default.CalculationInput)!;
                Assert.That(input.Cards[0].DrawOperation, Is.EqualTo(enabled ? DrawOperation.DangerReveal : DrawOperation.Ordinary));
                Assert.That(input.DrawOperationVersion, Is.EqualTo(enabled ? 1 : (int?)null));
                Assert.That(input.Cards[0].DrawCount, Is.Null);
                Assert.That(input.Cards[0].ExternalCardId, Is.EqualTo(canonicalId));
                ProbabilityCalculationResult result = CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json));
                Assert.That(result.TotalProbability, Is.EqualTo(1));
                Assert.That(result.ComboProbabilities.Single().Probability, Is.EqualTo(1));
                Assert.That(result.GroupProbabilities!.Single().Probability, Is.EqualTo(1));
            }
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task HistoricalDangerIdentityDoesNotReplaceSavedCustomOrOrdinaryPlans(bool draw) {
        SessionService codec = new(Mock.Of<IJSRuntime>(), new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());
        string operation = draw ? ",\"DrawCount\":2,\"DrawOncePerTurn\":false" : "";
        string json = $$"""{"SchemaVersion":13,"CalculationModel":"Fast","FastExecutionStrategy":"Progressive","Cards":[{"Id":"historical","Categories":[],"Copies":2,"Name":"Danger! Bigfoot!","ExternalCardId":43316238,"DrawOperation":"DangerReveal"{{operation}}}]}""";
        Card loaded = (await codec.LoadSessionAsync(json)).Cards.Single();
        Assert.That(loaded.EffectPlan.Operation, Is.EqualTo(DrawOperation.Ordinary));
        Assert.That(loaded.DrawCount, Is.EqualTo(draw ? 2 : (int?)null));
        Assert.That(loaded.EffectProfile, Is.EqualTo(EffectProfile.Legacy));
        Card explicitlyRestored = CardEffectRegistry.Default.RestoreBuiltIn(loaded);
        Assert.That(explicitlyRestored.EffectPlan.Operation, Is.EqualTo(DrawOperation.DangerReveal));
        Assert.That(explicitlyRestored.DrawCount, Is.Null);
    }

    private static IEnumerable<TestCaseData> SavedPreferences() => Enumerable.Range(0, 6)
        .SelectMany(schema => new[] { true, false }.Select(enabled => new TestCaseData(schema, enabled)));

    [TestCaseSource(nameof(SavedPreferences))]
    public async Task SchemaZeroThroughFiveKeepExistingPreferenceMigration(int schema, bool enabled) {
        SessionService codec = new(Mock.Of<IJSRuntime>(), new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());
        string json = $$"""{"SchemaVersion":{{schema}},"DrawEffectsEnabled":{{enabled.ToString().ToLowerInvariant()}},"Cards":[{"Id":"saved","Name":"Trade-In","ExternalCardId":38120068,"Categories":[],"Copies":1,"DrawCount":2,"PreDrawRemovalCount":0}]}""";
        SessionState loaded = await codec.LoadSessionAsync(json);
        // Draw preferences and effects first existed in v4; retain older migrations.
        Assert.That(loaded.CalculationModel, Is.EqualTo(enabled ? CalculationModel.Fast : CalculationModel.OpeningHand));
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
            Cards = custom, Categories = [role], HandSize = 2, CalculationModel = (enabled ? CalculationModel.Fast : CalculationModel.OpeningHand),
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
                Assert.That(loaded.CalculationModel, Is.EqualTo(enabled ? CalculationModel.Fast : CalculationModel.OpeningHand));
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
