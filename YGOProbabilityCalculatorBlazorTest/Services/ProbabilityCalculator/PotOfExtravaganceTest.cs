using System.Diagnostics;
using System.Text.Json;
using Microsoft.JSInterop;
using Moq;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;
using YGOProbabilityCalculatorBlazor.Services.Converter;
using YGOProbabilityCalculatorBlazor.Services.DeckImport;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.Session;
using YGOProbabilityCalculatorBlazor.Services.CalculationEngineAdapter;
using CP = CardProbability;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

public class PotOfExtravaganceTest {
    private static readonly CategoryBase A = new("A");
    private static readonly CategoryBase B = new("B");
    private static readonly CardEffectRegistry Registry = CardEffectRegistry.Default;
    private static Card Pot(int copies = 1) => Registry.RestoreBuiltIn(new([B], copies, id: "pot", externalCardId: 49238328));
    private static DrawActivationPriority Custom(params string[] ids) => new(DrawActivationMode.CustomPriority, ids);

    [Test]
    public void PhysicalFixturesProveOpeningWindowAtomicBatchAndRetainedBlockedCards() {
        Card pot = Pot();
        Card draw = new([], id: "draw", drawCount: 1);
        Card reward = new([A], id: "reward");
        List<Card> order = [draw, pot, draw.WithName("Second copy"), reward, new([], id: "f")];
        Assert.That(DrawSequenceOracle.Resolve(order, 2, [], ["pot", "draw"])!.Select(card => card.Id),
            Is.EqualTo(new[] { "draw", "draw", "reward" }));
        Assert.That(DrawSequenceOracle.Resolve(order, 2, [], ["draw", "pot"])!.Select(card => card.Id),
            Is.EqualTo(new[] { "pot", "draw" }));
        Assert.That(DrawSequenceOracle.Resolve([draw, pot, reward, new([], id: "f")], 1, [])!.Select(card => card.Id),
            Is.EqualTo(new[] { "pot" }), "A drawn Pot misses the opening window");
        Assert.That(DrawSequenceOracle.Resolve([pot, reward], 1, [])!.Single(), Is.SameAs(pot), "An unavailable Draw 2 stays in hand");
    }

    [TestCase(1), TestCase(2), TestCase(3)]
    public void PotCopiesAloneAndMixedEffectsMatchExactPhysicalOracle(int copies) {
        foreach (bool mixed in new[] { false, true }) {
            List<Card> deck = [Pot(copies), new([A, B], id: "target"), new([A], id: "material"), new([], 2, id: "f")];
            if (mixed) {
                deck.Add(new([B], id: "draw", drawCount: 1, drawOncePerTurn: false));
            }
            foreach (int hand in new[] { 1, 2, 3, deck.Sum(card => card.Copies) - 1, deck.Sum(card => card.Copies) }.Distinct()) {
                foreach (DrawActivationPriority priority in new[] { DrawActivationPriority.Default, Custom("pot", "draw"), Custom("draw", "pot") }) {
                    AssertParity(deck, hand, priority);
                }
            }
        }
    }

    [Test]
    public void ReversedPriorityHasDifferentExactResultsAndOrdinaryDrawTwoIsUnrestricted() {
        List<Card> deck = [Pot(), new([], id: "draw", drawCount: 1), new([A], 2, id: "target"), new([], id: "f")];
        ApplicationResultFractions potFirst = AssertParity(deck, 2, Custom("pot", "draw"));
        ApplicationResultFractions drawFirst = AssertParity(deck, 2, Custom("draw", "pot"));
        Assert.That(potFirst.Combos[1], Is.Not.EqualTo(drawFirst.Combos[1]));
        List<Combo> retainedPot = [new([], cards: [new("pot", 1, 1)])];
        Assert.That(Calculate(deck, retainedPot, 2, [], Custom("pot", "draw")).Total,
            Is.Not.EqualTo(Calculate(deck, retainedPot, 2, [], Custom("draw", "pot")).Total));
        Card custom = deck[0].WithEffect(DrawEffectPlan.Draw(2, deck[0].DrawOncePerTurn), deck[0].EffectProfile.AsCustom());
        Assert.That(custom.EffectPlan.HasActivationRestrictions, Is.False);
        Assert.That(custom.EffectProfile.Source, Is.EqualTo(EffectSource.Custom));
        AssertParity([custom, .. deck.Skip(1)], 2, Custom("pot", "draw"));
        AssertParity([new([B], name: "Pot of Extravagance", id: "pot", externalCardId: 49238328, drawCount: 2), .. deck.Skip(1)], 2, Custom("draw", "pot"));
    }

    [Test]
    public void DeferredAndFailedSourcesDoNotConsumeTheWindowAndRemovalFailureMassIsExact() {
        foreach (int variant in Enumerable.Range(0, 5)) {
            Card other = new([B], id: "draw", drawCount: 2,
                preDrawRemovalCount: variant <= 1 ? 1 : 0,
                preDrawSelector: variant <= 1 ? CardSelector.For(A) : null,
                postDrawDiscardCount: variant == 2 ? 2 : 0,
                postDrawSelector: variant == 2 ? CardSelector.For(A) : null,
                preDrawDeckRemovalCount: variant == 3 ? 2 : 0,
                postDrawDeckMillCount: variant == 4 ? 1 : 0);
            List<Card> deck = [Pot(), other, new(variant == 0 ? [] : [A], id: "material"), new([B], id: "target"), new([], 3, id: "f")];
            foreach (int hand in new[] { 1, 2, 4, 6 }) {
                AssertParity(deck, hand, Custom("draw", "pot"));
                AssertParity(deck, hand, DrawActivationPriority.Default);
            }
        }
    }

    [Test]
    public void ActualDesiresHiddenCostMatchesPhysicalPermutationMass() {
        Card desires = Registry.RestoreBuiltIn(new([], id: "draw", externalCardId: 35261759));
        List<Card> deck = [Pot(), desires, new([A], id: "target"), new([], 11, id: "f")];
        // 14 labeled cards, only 2,184 distinct identity permutations; each carries 11! weight.
        AssertParity(deck, 1, Custom("draw", "pot"));
        AssertParity(deck, 2, Custom("pot", "draw"));
        AssertParity(deck, 2, Custom("draw", "pot"));
    }

    [TestCase(true), TestCase(false)]
    public void CalculationAndWorkerUseTheSameSavedSemantics(bool configured) {
        List<Card> deck = [Pot(2), new([], id: "draw", drawCount: 1), new([A], 2, id: "target"), new([], 2, id: "f")];
        AssertParity(deck, 2, Custom("draw", "pot"), configured);
        Assert.That(DrawEffectPerformance.RequiresDetailedHandState(Pot().EffectPlan, EffectExecutionModel.DrawSequenceV1), Is.False);
        List<Combo> combos = [new([], cards: [new("pot", 1, 2)])];
        CalculationSnapshot opening = CalculationSnapshot.Capture(deck, combos, 2, [], drawEffectsEnabled: false);
        Assert.That(CalculationWire.ReadResult(CalculationWire.Execute(opening.Json)).TotalProbability,
            Is.EqualTo(new ProbabilityCalculatorService().CalculateProbabilityResults(deck, combos, 2, [], CalculationWorkPolicy.Interactive, options: new(Mode: CalculationModel.OpeningHand)).TotalProbability).Within(1e-12));
    }

    [Test]
    public async Task SnapshotsFreezeRevisionOneAndPreserveRevisionTwoThroughEditsAndSessions() {
        Card pot = Pot();
        Assert.That((pot.EffectProfile.Revision, pot.EffectProfile.Support), Is.EqualTo((2, EffectSupport.ModeledWithinRules)));
        Assert.That(Registry.CreateImportedCard(new CardInfo { Id = 49238328, Name = "Pot" }, 1, 49238328).EffectPlan, Is.EqualTo(pot.EffectPlan));
        Card old = new([B], id: "pot", externalCardId: 49238328, drawCount: 2,
            effectProfile: new(EffectSource.BuiltIn, 49238328, 1, "Draw 2; once per turn.", EffectSupport.Partial));
        SessionService codec = new(Mock.Of<IJSRuntime>(), new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());
        foreach (Card card in new[] { pot, old, old.WithDrawCount(2), new([], externalCardId: 49238328, drawCount: 2) }) {
            SessionState saved = new() { Cards = [card], HandSize = 1, DrawActivationPriority = Custom(card.Id) };
            SessionState loaded = await codec.LoadSessionAsync(codec.SerializeSession(saved));
            Assert.That(loaded.Cards.Single().EffectPlan, Is.EqualTo(card.EffectPlan));
            Assert.That(loaded.Cards.Single().EffectProfile, Is.EqualTo(card.EffectProfile));
            Assert.That(loaded.DrawActivationPriority.CardIds, Is.EqualTo(saved.DrawActivationPriority.CardIds));
            Assert.That(Registry.RestoreBuiltIn(loaded.Cards.Single()).EffectPlan, Is.EqualTo(pot.EffectPlan));
        }
        foreach (Card edited in new[] { pot.Duplicate(), pot.WithName("Renamed"), pot.WithCopies(3), pot.WithActive(false), pot.WithCategories([A]),
            pot.WithManualMetadataCategory(new("Property", CategorySource.Metadata, "test:value")), pot.WithObjectiveMetadata([], 49238328) }) {
            Assert.That(edited.EffectPlan, Is.EqualTo(pot.EffectPlan));
        }
        foreach (Card edited in new[] { pot.WithDrawCount(2), pot.WithDrawOncePerTurn(false), pot.WithPreDrawRemovalCount(1),
            pot.WithPostDrawDiscardCount(1), pot.WithPreDrawDeckRemovalCount(1), pot.WithPostDrawDeckMillCount(1), pot.WithDrawCount(null) }) {
            Assert.That(edited.EffectPlan.HasActivationRestrictions, Is.EqualTo(edited.DrawCount is not null));
        }
        string legacy = codec.SerializeSession(new() { Cards = [old], HandSize = 1 }).Replace("\"SchemaVersion\": 5", "\"SchemaVersion\": 4");
        Assert.That((await codec.LoadSessionAsync(legacy)).Cards.Single().EffectPlan.HasActivationRestrictions, Is.False);
    }

    [TestCase("FirstActivationOnly"), TestCase("BlocksLaterDraws")]
    public void RestrictionJsonRejectsDuplicateNonBooleanAndDrawlessFields(string field) {
        JsonSerializerOptions options = new();
        options.Converters.Add(new CardConverter());
        string card = "\"Categories\":[],\"Copies\":1,\"Name\":\"Pot\"";
        foreach (string extra in new[] { $"\"{field}\":1", $"\"{field}\":true,\"{field.ToLowerInvariant()}\":false", $"\"{field}\":true" }) {
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Card>("{" + card + "," + extra + "}", options));
        }
    }

    [Test]
    public void WorkerProjectionIgnoresInactiveAndZeroCopyEffects() {
        Card pot = Pot(3);
        List<Card> zeroCopy = [pot, new([], 0, id: "zero", drawCount: 1), new([], 37, id: "f")];
        List<Card> inactive = [pot, new([], active: false, id: "inactive", drawCount: 1), new([], 37, id: "f")];
        foreach (List<Card> cards in new[] { zeroCopy, inactive }) {
            CalculationSnapshot snapshot = CalculationSnapshot.Capture(cards, [new([], cards: [new(pot.Id, 1, 3)])], 5, [], options: new());
            Assert.DoesNotThrow(() => CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json)));
        }
    }

    [TestCase(false), TestCase(true)]
    public async Task ExampleFuwalosReplacementFitsStandardStorageAndMatchesOptDrawTwo(bool customPriority) {
        SessionService codec = new(Mock.Of<IJSRuntime>(), new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());
        SessionState session = await codec.LoadSessionAsync(File.ReadAllText(Path.Combine(
            TestContext.CurrentContext.TestDirectory, "Fixtures", "example_session_state.json")));
        Card original = session.Cards.Single(card => card.Name == "Mulcharmy Fuwalos");
        Card pot = Registry.RestoreBuiltIn(new([], original.Copies, id: original.Id, externalCardId: 49238328));
        session.Cards[session.Cards.IndexOf(original)] = pot;
        List<Card> deck = CategoryMembership.Materialize(session.Cards, session.Categories).Where(card => card.Active).ToList();
        List<Combo> combos = session.Combos.Where(combo => combo.Active).ToList();
        DrawActivationPriority priority = customPriority ? Custom(pot.Id) : DrawActivationPriority.Default;
        Assert.That(pot.Copies, Is.EqualTo(3));
        Assert.That(deck.Sum(card => card.Copies), Is.EqualTo(40));
        ApplicationResultFractions actual = Calculate(deck, combos, 5, session.ComboGroups, priority);
        List<Card> generic = deck.Select(card => card.Id == pot.Id ? card.WithEffect(DrawEffectPlan.Draw(2, card.DrawOncePerTurn), card.EffectProfile.AsCustom()) : card).ToList();
        ApplicationResultFractions expected = Calculate(generic, combos, 5, session.ComboGroups, priority);
        Assert.That(actual.Total, Is.EqualTo(expected.Total));
        Assert.That(actual.Combos, Is.EqualTo(expected.Combos));
        Assert.That(actual.Groups, Is.EqualTo(expected.Groups));
        CalculationSnapshot snapshot = CalculationSnapshot.Capture(session.Cards, combos, 5, session.ComboGroups,
            storagePolicy: CalculationStoragePolicy.Standard, options: new(priority), categoryDefinitions: session.Categories);
        ProbabilityCalculationResult worker = CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json));
        Assert.That(worker.TotalProbability, Is.EqualTo(actual.Total.ToDouble()).Within(1e-12));
        Assert.That(worker.ComboProbabilities.Select(row => row.Probability),
            Is.EqualTo(actual.Combos.Select(row => row.ToDouble())));
        Assert.That(pot.EffectPlan.HasActivationRestrictions, Is.True, "Fast-path proof must not rewrite saved semantics");
    }

    [Test, Explicit("40-card/5-hand repeatable mixed-effect benchmark")]
    public void BenchmarkMixedFortyCardDeck() {
        List<Card> deck = [Pot(3), Registry.RestoreBuiltIn(new([], 2, id: "desires", externalCardId: 35261759)),
            new([], 2, id: "cost", drawCount: 2, preDrawRemovalCount: 1, preDrawSelector: CardSelector.For(B)),
            new([A], 8, id: "target"), new([B], 6, id: "material"), new([A, B], 4, id: "both"), new([], 15, id: "f")];
        List<Combo> combos = [new([new(A, 1, 5)]), new([], cards: [new("pot", 1, 3)])];
        foreach (bool restricted in new[] { false, true }) {
            List<Card> modeled = restricted ? deck : deck.Select(card => card.Id == "pot" ? card.WithEffect(card.EffectPlan with { FirstActivationOnly = false, BlocksLaterDraws = false }, card.EffectProfile.AsCustom()) : card).ToList();
            foreach (DrawActivationPriority priority in new[] { DrawActivationPriority.Default, Custom("pot", "desires", "cost") }) {
                CP.CalculationRequest request = new CalculationRequestAdapter(modeled, true).Create(combos, 5);
                Stopwatch watch = Stopwatch.StartNew();
                CP.CalculationResult result = CP.CalculationEngine.Calculate(request,
                    new(CP.CalculationMode.Exact, new(CalculationWorkPolicy.Interactive.WorkUnits),
                        priority.Mode == DrawActivationMode.CustomPriority ? CP.ActivationOrder.Priority : CP.ActivationOrder.EncounterOrder,
                        priority.CardIds));
                Assert.That(result.Status, Is.EqualTo(CP.CalculationStatus.Completed));
                TestContext.Out.WriteLine($"restricted={restricted}, priority={priority.Mode}, ms={watch.Elapsed.TotalMilliseconds:F1}, work={result.Metrics.WorkSpent}, total={result.Total!.Primary}");
            }
        }
    }

    private static ApplicationResultFractions Calculate(
        List<Card> deck,
        List<Combo> combos,
        int hand,
        IReadOnlyList<ComboGroup> groups,
        DrawActivationPriority priority
    ) => ApplicationResultFractions.From(new ProbabilityCalculatorService().CalculateProbabilityResults(
            deck, combos, hand, groups, CalculationWorkPolicy.Interactive, options: new(priority)));

    private static ApplicationResultFractions AssertParity(List<Card> deck, int hand, DrawActivationPriority priority, bool configured = true) {
        List<Combo> combos = [new([new(A, 1, 5)], groupId: "g"), new([], cards: [new("pot", 1, 3)], groupId: "g"),
            new([new(B, 1, 5)], cards: [new("target", 1, 1)]), new([new(A, 0, 0)], cards: [new("pot", 1, 3)])];
        List<ComboGroup> groups = [new("g", "Group"), new("empty", "Empty")];
        List<Card> modeled = configured ? deck : deck.Select(card => card.WithPreDrawRemovalCount(0).WithPostDrawDiscardCount(0)
            .WithPreDrawDeckRemovalCount(0).WithPostDrawDeckMillCount(0)).ToList();
        DrawSequenceOracle.Counts oracle = DrawSequenceOracle.Enumerate(modeled, hand, combos, groups, true,
            priority.Mode == DrawActivationMode.CustomPriority ? priority.CardIds : null);
        ApplicationResultFractions result = Calculate(modeled, combos, hand, groups, priority);
        ApplicationFraction[] rows = [result.Total, .. result.Combos, .. result.Groups];
        for (int i = 0; i < rows.Length; i++) {
            Assert.That(rows[i].Numerator * oracle.Total, Is.EqualTo(oracle.Wins[i] * rows[i].Denominator), $"row={i}, hand={hand}, priority={priority.Mode}");
        }
        CalculationSnapshot snapshot = CalculationSnapshot.Capture(modeled, combos, hand, groups, options: new(priority));
        ProbabilityCalculationResult worker = CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json));
        Assert.That(worker.TotalProbability, Is.EqualTo(rows[0].ToDouble()).Within(1e-12));
        for (int i = 0; i < result.Combos.Length; i++) {
            Assert.That(worker.ComboProbabilities[i].Probability, Is.EqualTo(result.Combos[i].ToDouble()).Within(1e-12));
        }
        foreach (GroupProbabilityResult group in worker.GroupProbabilities!) {
            int index = groups.FindIndex(candidate => candidate.Id == group.GroupId);
            Assert.That(group.Probability, Is.EqualTo(result.Groups[index].ToDouble()).Within(1e-12));
        }
        return result;
    }
}
