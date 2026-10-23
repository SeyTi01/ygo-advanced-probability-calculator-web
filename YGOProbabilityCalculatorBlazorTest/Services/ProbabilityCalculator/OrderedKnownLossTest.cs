using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.Session;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Counting;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.DrawResolution;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Search;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture, NonParallelizable]
public sealed class OrderedKnownLossTest {
    [TestCase(0, false, false)] [TestCase(1, false, false)] [TestCase(2, false, false)]
    [TestCase(1, true, false)] [TestCase(2, true, false)]
    [TestCase(0, false, true)] [TestCase(1, false, true)] [TestCase(2, false, true)]
    [TestCase(1, true, true)] [TestCase(2, true, true)]
    public void PaymentsSourcesAndMergedHistoriesBoundChronologicalOracle(int payments, bool searchable, bool hidden) {
        CategoryBase material = new("Material");
        CategoryBase target = new("Target");
        List<Card> cards = [
            new(searchable ? [target] : [], 2, name: "Same name", id: "draw", drawCount: 1,
                drawOncePerTurn: false, preDrawRemovalCount: 1, preDrawSelector: CardSelector.For(material)),
            new([], 1, id: "hidden", drawCount: 1, preDrawDeckRemovalCount: hidden ? 1 : 0),
            new([], 1, id: "search", search: new(CardSelector.For(target), false)),
            new([target], 1, id: "target"),
            new(searchable ? [material, target] : [material], payments, name: "Same name", id: "payment"),
            new([], 3 - payments, id: "filler")
        ];
        if (searchable && payments == 2) {
            cards[^2] = cards[^2].WithCopies(1);
            cards.Add(new([material], 1, name: "Same name", id: "other-payment"));
        }
        cards = cards.Where(card => card.Copies > 0).ToList();
        List<ComboGroup> groups = [new("empty", "Empty"), new("g", "Union")];
        List<Combo> combos = [new([new(target, 1, 3)], groupId: "g"),
            new([new(target, 1, 3), new(material, 1, 3)], groupId: "g"),
            new([ComboCategory.SameCard([material, target], 1, 1)], groupId: "g"),
            new([new(target, 0, 0)]),
            new([], cards: [new("draw", 0, 0), new("search", 0, 0)]),
            new([], groupId: "g", alternativeGroups: [new([
                ComboAlternative.For(new ComboCard("payment", 1, 2)),
                ComboAlternative.For(new ComboCategory(target, 2, 3))])])];
        string[] order = ["hidden", "draw"];
        Check(cards, 2, combos, groups, order);
    }

    [TestCase(2, false)] [TestCase(3, true)] [TestCase(6, true)]
    public void PostRemovalSharedPaymentsSearchableSourcesAndExhaustion(int hand, bool postRemoval) {
        CategoryBase material = new("Material");
        CategoryBase target = new("Target");
        List<Card> cards = [
            new([target], 1, id: "first", drawCount: 1, preDrawRemovalCount: 1,
                preDrawSelector: CardSelector.For(material), postDrawDeckMillCount: 1),
            new([], 1, id: "second", drawCount: 1, preDrawRemovalCount: postRemoval ? 0 : 1,
                preDrawSelector: postRemoval ? null : CardSelector.For(material), postDrawDiscardCount: postRemoval ? 1 : 0,
                postDrawSelector: postRemoval ? CardSelector.For(material) : null),
            new([], 1, id: "hidden", drawCount: 1, preDrawDeckRemovalCount: 1, postDrawDeckMillCount: 1),
            new([material, target], 1, id: "payment"),
            new(postRemoval ? [material] : [], 1, id: "search", search: new(CardSelector.For(target), false)),
            new([], 1, id: "other-search", search: new(CardSelector.For(target), false)),
            new([target], 1, id: "target"), new([], 1, id: "filler")
        ];
        List<Combo> combos = [new([new(target, 1, 1)]), new([new(material, 0, 0)]),
            new([], cards: [new("first", 1, 8), new("search", 0, 0)])];
        Check(cards, hand, combos, [], ["second", "hidden", "first"]);
    }

    [Test]
    public void MandatoryPaymentStillDestroysAnEstablishedCombo() {
        CategoryBase material = new("Material");
        CategoryBase target = new("Target");
        Card draw = new([], 1, id: "draw", drawCount: 1, preDrawRemovalCount: 1, preDrawSelector: CardSelector.For(material));
        Card payment = new([material], 1, id: "payment");
        Card filler = new([], 1, id: "filler");
        Card search = new([], 1, id: "search", search: new(CardSelector.For(target), false));
        List<Combo> combos = [new([new(material, 1, 4)])];
        Assert.That(SearchCertificateHandOracle.MatchesHand([draw, payment], combos[0]), Is.True);
        List<Card>? final = DrawSequenceOracle.Resolve([draw, payment, filler, search], 2, combos);
        Assert.That(final, Is.Not.Null);
        Assert.That(SearchCertificateHandOracle.MatchesHand(final!, combos[0]), Is.False);
        Check([draw, payment, filler, search], 2, combos, [], null);
    }

    [Test]
    public void LossTableTakesWorstCompatibleHistoryAndChargesLimits() {
        CategoryBase target = new("Target");
        CategoryBase material = new("Material");
        List<Card> cards = [new([], 1, id: "known", drawCount: 1, preDrawRemovalCount: 1,
                preDrawSelector: CardSelector.For(material)),
            new([], 1, id: "unknown", drawCount: 1, postDrawDeckMillCount: 1),
            new([material], 1, id: "payment"), new([target], 2, id: "target"),
            new([], 1, id: "search", search: new(CardSelector.For(target), false))];
        int[] loss = OrderedSearchLossBudget.Create(cards, new(CalculationWorkPolicy.Default));
        Assert.That(loss[2], Is.EqualTo(1), "Either source explains D=2; never select only the known-payment history.");
        Assert.That(loss[4], Is.EqualTo(1));
        Assert.That(loss[1], Is.EqualTo(1), "Unexplained totals retain the scalar fallback.");
        Assert.Throws<ProbabilityCalculationLimitException>(() => OrderedSearchLossBudget.Create(cards, new(new(1))));
        List<Card> oversized = [new([], 40_000, id: "large")];
        ProbabilityCalculationLimitException exception = Assert.Throws<ProbabilityCalculationLimitException>(() =>
            OrderedSearchLossBudget.Create(oversized, new(CalculationWorkPolicy.Default)))!;
        Assert.That(exception.Reason, Is.EqualTo(ProbabilityCalculationLimitReason.Storage));
    }

    [Test]
    public async Task FrozenSourceTightensAllRowsWithIdenticalUpperAndTerminalDistribution() {
        SessionState state = await SelectiveSurvivorTest.Source();
        List<Card> searches = state.Cards.Where(card => card.Search is not null).ToList();
        Assert.That(state.Cards.Where(card => card.DrawCount is not null)
            .Any(source => searches.Any(search => search.Search!.Matches(source))), Is.False);
        int[] loss = OrderedSearchLossBudget.Create(state.Cards, new(CalculationWorkPolicy.Interactive));
        Assert.That(loss[2], Is.Zero, "The source/payment-only outcomes have no searchable loss.");
        Assert.That(loss[13], Is.EqualTo(10), "Hidden banishment remains uncertain after source/payment consumption.");
        WorkBudget budget = new(CalculationWorkPolicy.Interactive);
        PostDrawCalculation execution = new(state.Cards, state.HandSize, budget, state.DrawActivationPriority);
        ExactCalculationResult upper = execution.CalculateSearch(state.Combos, state.ComboGroups, false, new(state.Cards, budget));
        int terminals = execution.TerminalStates;
        ExactCalculationResult old = execution.CalculateSearchLower(state.Combos, state.ComboGroups, false, useKnownLoss: false);
        ExactCalculationResult refined = execution.CalculateSearchLower(state.Combos, state.ComboGroups, false);
        Assert.That(old.Total.ToFraction(), Is.EqualTo(new ProbabilityFraction("61232447131", "68906597760")));
        Assert.That(refined.Total.ToFraction(), Is.EqualTo(new ProbabilityFraction("61422732523", "68906597760")));
        Assert.That(upper.Total.ToFraction(), Is.EqualTo(new ProbabilityFraction("63931285207", "68906597760")));
        Assert.That(execution.TerminalStates, Is.EqualTo(terminals));
        for (int row = 0; row < Rows(old).Length; row++) {
            LessOrEqual(Rows(old)[row], Rows(refined)[row], $"source row {row}");
        }
        TestContext.Out.WriteLine($"Terminals={terminals}; reduced={execution.ReducedRows}; deferred={execution.DeferredRows}");
    }

    private static void Check(List<Card> cards, int hand, List<Combo> combos, List<ComboGroup> groups, string[]? order) {
        WorkBudget budget = new(CalculationWorkPolicy.Interactive);
        DrawActivationPriority? priority = order is null ? null : new(DrawActivationMode.CustomPriority, order);
        PostDrawCalculation execution = new(cards, hand, budget, priority);
        ExactCalculationResult upper = execution.CalculateSearch(combos, groups, false, new(cards, budget));
        ExactCalculationResult old = execution.CalculateSearchLower(combos, groups, false, useKnownLoss: false);
        ExactCalculationResult refined = execution.CalculateSearchLower(combos, groups, false);
        SearchCertificateOracle.Counts oracle = SearchCertificateOracle.Enumerate(cards, hand, combos, groups, order);
        for (int row = 0; row < Rows(upper).Length; row++) {
            ExactProbability physical = new(oracle.Wins[1][row], oracle.Outcomes);
            LessOrEqual(Rows(old)[row], Rows(refined)[row], $"old <= new {row}");
            LessOrEqual(Rows(refined)[row], physical, $"new <= physical {row}");
            LessOrEqual(physical, Rows(upper)[row], $"physical <= upper {row}");
        }
    }

    private static void LessOrEqual(ExactProbability left, ExactProbability right, string label) =>
        Assert.That(left.Numerator * right.Denominator, Is.LessThanOrEqualTo(right.Numerator * left.Denominator), label);

    private static ExactProbability[] Rows(ExactCalculationResult result) => [result.Total, .. result.Combos, .. result.Groups];
}
