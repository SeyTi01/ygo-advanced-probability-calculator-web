using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public sealed class ExcavationTest {
    [TestCase(3, 4, "4", "15"), TestCase(6, 6, "13", "56")]
    public void RevealedPoolIsWeightedAndSelectedActivationCannotBeDeclined(int count, int fillerCopies, string numerator, string denominator) {
        List<Card> deck = [Excavate("pot", count), new([], id: "target"), new([], fillerCopies, id: "filler")];
        List<Combo> combos = [Direct("target"), Direct("pot")];
        ProbabilityCalculationResult result = Verify(deck, 1, combos);
        Assert.That(result.Contract!.Fractions!.Combos[0], Is.EqualTo(new ProbabilityFraction(numerator, denominator)));
        Assert.That(result.Contract.Fractions.Combos[1], Is.EqualTo(new ProbabilityFraction("0", "1")));
    }

    [Test]
    public void OverlappingPoolsSearchesAndRowsSharePhysicalCopies() {
        CategoryBase a = new("A");
        CategoryBase b = new("B");
        List<Card> deck = [Excavate("pot", 3),
            new([], id: "search", search: new(CardSelector.AnyOf([a.Identity, b.Identity]), false)),
            new([a], id: "a"), new([b], id: "b"), new([a, b], 2, id: "both"), new([], 2, id: "filler")];
        List<Combo> combos = [
            new([new(a, 1, 2), new(b, 1, 2)], "Distinct roles", groupId: "roles"),
            new([new(a, 0, 0)], "No A", groupId: "exclusions"),
            new([new(a, 1, 1), new(b, 1, 2)], "At most one A", groupId: "roles"),
            new([ComboCategory.SameCard([a, b], 1, 1)], "Same card", groupId: "roles"),
            new([], "Direct and role", groupId: "roles", cards: [new("both", 1, 2)],
                alternativeGroups: [new([ComboAlternative.For(new ComboCategory(a, 1, 2)),
                    ComboAlternative.For(new ComboCategory(b, 1, 2))])]),
            new([], "Retained source", groupId: "exclusions", cards: [new("pot", 1, 1)])
        ];
        Verify(deck, 2, combos, [new("roles", "Roles"), new("exclusions", "Exclusions"), new("empty", "Empty")],
            new Dictionary<string, string[]> { ["search"] = ["a", "b", "both"] }, wire: true);
    }

    [TestCase(false), TestCase(true)]
    public void HighestEligibleAmountWinsAndTiesUseStableEntryIdentity(bool tied) {
        List<Card> deck = [Excavate("z-high", tied ? 2 : 3), Excavate("a-low", 2).WithCopies(2),
            new([], id: "target"), new([], 3, id: "filler")];
        List<Combo> combos = [Direct("z-high"), Direct("a-low"), Direct("target"),
            new([], "Retain both", cards: [new("z-high", 1, 2), new("a-low", 1, 2)])];
        ProbabilityCalculationResult original = Verify(deck, 2, combos);
        DrawActivationPriority priority = new(DrawActivationMode.CustomPriority, ["z-high", "a-low"]);
        ProbabilityCalculationResult reversed = Verify(deck.AsEnumerable().Reverse().ToList(), 2, combos, priority: priority);
        Assert.That(reversed.Contract!.Fractions!.Total, Is.EqualTo(original.Contract!.Fractions!.Total));
        Assert.That(reversed.Contract.Fractions.Combos, Is.EqualTo(original.Contract.Fractions.Combos));
    }

    [TestCase(2), TestCase(5)]
    public void InsufficientDeckSkipsIneligibleSourceBeforeApplyingPriority(int handSize) {
        List<Card> deck = [Excavate("high", 6), Excavate("low", 3), new([], id: "target"), new([], 4, id: "filler")];
        Verify(deck, handSize, [Direct("high"), Direct("low"), Direct("target")]);
    }

    [TestCase(0, 0), TestCase(1, 0), TestCase(0, 1), TestCase(1, 1)]
    public void CompletedDrawsAndHiddenLossesPrecedeExcavationAndDisableRestrictedSource(int banish, int mill) {
        CategoryBase target = new("Target");
        List<Card> deck = [Excavate("restricted", 3, true), Excavate("unrestricted", 2),
            new([], id: "draw", drawCount: 1, preDrawDeckRemovalCount: banish, postDrawDeckMillCount: mill),
            new([], id: "search", search: new(CardSelector.For(target), false)),
            new([target], 2, id: "target"), new([], 2, id: "filler")];
        Verify(deck, 2, [Direct("restricted"), Direct("unrestricted"), Direct("draw"),
            new([new(target, 1, 2)], "Target"), new([new(target, 0, 0)], "No target")],
            searchTargets: new Dictionary<string, string[]> { ["search"] = ["target"] });
    }

    [TestCase(false), TestCase(true)]
    public void UnchosenPoolCardsRemainSearchableAndFetchedEffectsDoNotActivate(bool bounded) {
        CategoryBase target = new("Target");
        List<Card> deck = [Excavate("pot", 3),
            new([], id: "search", search: new(CardSelector.For(target), false), minimumHandSize: bounded ? 2 : 0),
            new([target], id: "target"), new([], id: "draw", drawCount: 1), new([], 3, id: "filler")];
        Verify(deck, 2, [Direct("target"), Direct("search"), Direct("draw"),
            new([], "Target and drawn source", cards: [new("target", 1, 1), new("draw", 1, 1)]),
            new([new(target, 0, 0)], "No target")],
            searchTargets: new Dictionary<string, string[]> { ["search"] = ["target"] });
    }

    [TestCase(false), TestCase(true)]
    public void ExcavatedCardCanPayAnOriginalSearchCostOrMandatoryRemoval(bool afterSearch) {
        CategoryBase target = new("Target");
        CategoryBase cost = new("Unique cost");
        List<Card> deck = [Excavate("pot", 3),
            new([], id: "search", search: new(CardSelector.For(target), false),
                preDrawRemovalCount: afterSearch ? 0 : 1, preDrawSelector: afterSearch ? null : CardSelector.For(cost),
                postDrawDiscardCount: afterSearch ? 1 : 0, postDrawSelector: afterSearch ? CardSelector.For(cost) : null),
            new([target], id: "target"), new([cost], id: "cost"), new([], 2, id: "filler")];
        Verify(deck, 2, [new([new(target, 1, 1)], "Target", cards: [new("search", 0, 0)]),
            Direct("cost"), Direct("search"), new([new(target, 0, 0)], "No target")],
            searchTargets: new Dictionary<string, string[]> { ["search"] = ["target"] });
    }

    [TestCase(false), TestCase(true)]
    public void DisabledAndInactiveEntriesCannotWinExcavationPriority(bool inactive) {
        Card high = Excavate("high", 3);
        high = inactive ? high.WithActive(false) : high.WithSimulationEnabled(false);
        List<Card> deck = [high, Excavate("low", 2), new([], id: "target"), new([], 3, id: "filler")];
        Verify(deck, 2, [Direct("high"), Direct("low"), Direct("target")]);
    }

    [Test]
    public void DrawLockDoesNotBlockUnrestrictedExcavationAndOpeningHandModeIgnoresIt() {
        List<Card> deck = [Excavate("pot", 3), new([], id: "draw", drawCount: 1, blocksLaterDraws: true),
            new([], id: "target"), new([], 4, id: "filler")];
        List<Combo> combos = [Direct("target"), Direct("pot")];
        Verify(deck, 2, combos);
        ProbabilityCalculationResult opening = new ProbabilityCalculatorService().CalculateProbabilityResults(deck, combos, 2, null,
            CalculationWorkPolicy.Interactive, new(Mode: CalculationModel.OpeningHand));
        Assert.That(opening.TotalProbability, Is.EqualTo(SmallDeckOracle.EnumerateProbability(deck, combos, 2)).Within(1e-15));
    }

    internal static Card Excavate(string id, int count, bool restricted = false) =>
        new([], id: id, excavation: new(count, restricted));

    private static Combo Direct(string id) => new([], id, cards: [new(id, 1, 60)]);

    private static ProbabilityCalculationResult Verify(
        List<Card> deck,
        int handSize,
        List<Combo> combos,
        List<ComboGroup>? groups = null,
        IReadOnlyDictionary<string, string[]>? searchTargets = null,
        DrawActivationPriority? priority = null,
        bool wire = false
    ) {
        groups ??= [];
        searchTargets ??= new Dictionary<string, string[]>();
        ExcavationOracle.Counts expected = ExcavationOracle.Count(deck, handSize, combos, groups, searchTargets, priority);
        ProbabilityCalculationResult exact = null!;
        foreach (CalculationModel mode in new[] { CalculationModel.Fast, CalculationModel.Exact }) {
            CalculationOptions options = new(priority, Mode: mode);
            exact = new ProbabilityCalculatorService().CalculateProbabilityResults(deck, combos, handSize, groups,
                CalculationWorkPolicy.Interactive, options);
            PhysicalSearchTest.AssertRows(exact, expected.Wins, expected.Outcomes);
            Assert.That(exact.Contract!.Accuracy, Is.EqualTo(CalculationAccuracy.Exact), mode.ToString());
            if (wire) {
                CalculationSnapshot snapshot = CalculationSnapshot.Capture(deck, combos, handSize, groups,
                    CalculationWorkPolicy.Interactive, options: options);
                CalculationResponse response = JsonSerializer.Deserialize(CalculationWire.Execute(snapshot.Json),
                    CalculationJsonContext.Default.CalculationResponse)!;
                Assert.That(response.Result, Is.Not.Null);
                PhysicalSearchTest.AssertRows(response.Result!, expected.Wins, expected.Outcomes);
            }
        }
        TestContext.Out.WriteLine($"excavation oracle: [{string.Join(',', expected.Wins)}] / {expected.Outcomes}");

        return exact;
    }
}
