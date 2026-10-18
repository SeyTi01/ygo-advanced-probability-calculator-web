using System.Numerics;
using Microsoft.JSInterop;
using Moq;
using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.Session;
using YGOProbabilityCalculatorBlazor.Services.Interface;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

public sealed class HandEffectActivationEligibilityTest {
    [TestCase(2, false), TestCase(3, false), TestCase(2, true), TestCase(3, true)]
    public void MinimumHandRuleMatchesIndependentLabeledOrderOracle(int handSize, bool growth) {
        Card source = CardEffectRegistry.Default.RestoreBuiltIn(new([], id: "void", externalCardId: 93946239));
        Card draw = growth ? new([], id: "grow", drawCount: 2) : new([], id: "blank");
        List<Card> cards = [source, draw, new([], id: "goal"), new([], 2, id: "filler")];
        List<Combo> combos = [new([], cards: [new("goal", 1, 5)])];
        (int wins, int total) = Enumerate(cards, handSize);
        foreach (CalculationModel mode in new[] { CalculationModel.Fast, CalculationModel.Exact }) {
            ProbabilityCalculationResult result = new ProbabilityCalculatorService().CalculateProbabilityResults(cards, combos, handSize, null, CalculationWorkPolicy.Default, new(Mode: mode));
            CalculationSnapshot snapshot = CalculationSnapshot.Capture(cards, combos, handSize, [], options: new(Mode: mode));
            ProbabilityCalculationResult wire = CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json), mode);
            Assert.That(wire.Contract!.Fractions!.Total, Is.EqualTo(result.Contract!.Fractions!.Total));
            Assert.That(result.Contract!.Completion, Is.EqualTo(CalculationCompletion.Success));
            Assert.That(BigInteger.Parse(result.Contract!.Fractions!.Total.Numerator) * total,
                Is.EqualTo(new BigInteger(wins) * BigInteger.Parse(result.Contract!.Fractions!.Total.Denominator)));
        }
        if (!growth) {
            Assert.That(wins * (handSize == 2 ? 5 : 4), Is.EqualTo(total * (handSize == 2 ? 2 : 3)));
        }
    }

    // Each position is a distinct physical copy. This independent tiny-deck
    // interpreter implements only the two fixture operations, with no engine
    // selectors, routing, event compiler, or production hand evaluator.
    private static (int Wins, int Total) Enumerate(List<Card> cards, int handSize) {
        List<Card> labels = cards.SelectMany(card => Enumerable.Repeat(card, card.Copies)).ToList();
        int[] permutation = Enumerable.Range(0, labels.Count).ToArray();
        int wins = 0;
        int total = 0;
        Visit(0);
        return (wins, total);

        void Visit(int offset) {
            if (offset < permutation.Length) {
                for (int index = offset; index < permutation.Length; index++) {
                    (permutation[offset], permutation[index]) = (permutation[index], permutation[offset]);
                    Visit(offset + 1);
                    (permutation[offset], permutation[index]) = (permutation[index], permutation[offset]);
                }
                return;
            }
            total++;
            List<Card> hand = permutation.Take(handSize).Select(index => labels[index]).ToList();
            int cursor = handSize;
            while (true) {
                int source = hand.FindIndex(card => (card.Id == "void" && hand.Count >= 3 && cursor < labels.Count)
                    || (card.Id == "grow" && cursor + 2 <= labels.Count));
                if (source < 0) {
                    break;
                }
                int amount = hand[source].Id == "grow" ? 2 : 1;
                hand.RemoveAt(source);
                for (int draw = 0; draw < amount; draw++) {
                    hand.Add(labels[permutation[cursor++]]);
                }
            }
            if (hand.Any(card => card.Id == "goal")) {
                wins++;
            }
        }
    }

    [Test]
    public async Task NewFieldsSurviveAllCopiesSessionShareAndWorkerWhileOldSnapshotStaysOld() {
        SessionService codec = new(Mock.Of<IJSRuntime>(), new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());
        foreach (int identity in new[] { 93946239, 78058681, 67115133, 36637374, 98567237 }) {
            Card card = CardEffectRegistry.Default.RestoreBuiltIn(new([], 2, id: "source", externalCardId: identity));
            foreach (Card copy in new[] { card.Duplicate(), card.WithName("renamed"), card.WithCopies(3), card.WithActive(false),
                card.WithCategories(card.Categories), card.WithObjectiveMetadata([], identity) }) {
                Assert.That(copy.EffectPlan, Is.EqualTo(card.EffectPlan));
            }
            string json = codec.SerializeSession(new() { Cards = [card], HandSize = 1 });
            string link = SessionShareCodec.CreateLink("https://example.invalid/", json);
            SessionState restored = await codec.LoadSessionAsync(SessionShareCodec.Decode(new Uri(link).Fragment));
            Assert.That(restored.Cards[0].EffectPlan, Is.EqualTo(card.EffectPlan));
            CalculationInput worker = JsonSerializer.Deserialize<CalculationInput>(CalculationSnapshot.Capture([card], [], 1, []).Json)!;
            Assert.Multiple(() => {
                Assert.That(worker.Cards[0].ActivationLimit, Is.EqualTo(card.EffectPlan.ActivationLimit));
                Assert.That(worker.Cards[0].MinimumHandSize, Is.EqualTo(card.MinimumHandSize));
                Assert.That(worker.Cards[0].Search, Is.EqualTo(card.Search));
            });
        }
        Card old = new([], externalCardId: 93946239, drawCount: 1, drawOncePerTurn: false,
            effectProfile: new(EffectSource.BuiltIn, 93946239, 1, "Draw 1", EffectSupport.Partial, "Minimum hand size is not checked."));
        System.Text.Json.Nodes.JsonObject oldRoot = System.Text.Json.Nodes.JsonNode.Parse(codec.SerializeSession(new() { Cards = [old] }))!.AsObject();
        oldRoot["SchemaVersion"] = 8;
        oldRoot.Remove("FastExecutionStrategy");
        string oldJson = oldRoot.ToJsonString();
        Card loaded = (await codec.LoadSessionAsync(oldJson)).Cards[0];
        Assert.That(loaded.EffectPlan, Is.EqualTo(old.EffectPlan));
        Assert.That(loaded.MinimumHandSize, Is.Zero);
        Assert.That(CardEffectRegistry.Default.RestoreBuiltIn(loaded).MinimumHandSize, Is.EqualTo(3));
    }

    [Test]
    public void SharedTwoActivationLimitIsAppliedToDrawsAsWellAsSearches() {
        Card effect = new Card([], 3, id: "effect", drawCount: 1, drawOncePerTurn: false)
            .WithEffect(DrawEffectPlan.Draw(1, false) with { ActivationLimit = 2 }, EffectProfile.Legacy);
        WorkBudget budget = new(CalculationWorkPolicy.Interactive);
        DrawResolution resolution = DrawEffectResolver.Resolve(3, [effect], 3, budget);
        // Three seen sources consume only two copies, leaving one in every
        // terminal whose original source count is three.
        Assert.That(resolution.Scenarios.Keys.Any(state => state.EffectCounts[0] == 1), Is.True);
        Assert.That(DrawEffectResolver.CanUseCountStateFastPath([effect], 6, 3), Is.True);
        List<Combo> combos = [new([], cards: [new(effect.Id, 1, 3)])];
        ProbabilityCalculationResult limited = new ProbabilityCalculatorService().CalculateProbabilityResults([effect, new([], 3, id: "filler")], combos, 3);
        ProbabilityCalculationResult unlimited = new ProbabilityCalculatorService().CalculateProbabilityResults([effect.WithOncePerTurn(false), new([], 3, id: "filler")], combos, 3);
        Assert.That(BigInteger.Parse(limited.Contract!.Fractions!.Total.Numerator), Is.GreaterThan(BigInteger.Zero));
        Assert.That(BigInteger.Parse(unlimited.Contract!.Fractions!.Total.Numerator), Is.EqualTo(BigInteger.Zero));
    }
}
