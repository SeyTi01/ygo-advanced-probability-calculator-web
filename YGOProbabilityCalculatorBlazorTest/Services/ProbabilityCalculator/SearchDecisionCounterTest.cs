using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public sealed class SearchDecisionCounterTest {
    [Test]
    public void SeededPredicateUnionsMatchLabeledHandsAtEverySize() {
        Random random = new(2042026);
        for (int trial = 0; trial < 80; trial++) {
            List<Card> deck = Enumerable.Range(0, 5).Select(row => new Card([], random.Next(3), id: $"r{row}")).ToList();
            int[] physical = deck.SelectMany((card, row) => Enumerable.Repeat(row, card.Copies)).ToArray();
            for (int size = 0; size <= physical.Length; size++) {
                List<CompiledEvent?> predicates = Enumerable.Range(0, random.Next(1, 9)).Select(_ => (CompiledEvent?)new CompiledEvent(
                    Enumerable.Range(0, random.Next(5)).Select(_ => {
                        int min = random.Next(size + 2);
                        return new CountBound(random.Next(32), min, random.Next(min, size + 3));
                    }).ToArray())).ToList();
                predicates.Add(null);
                WorkBudget budget = new(CalculationWorkPolicy.Interactive);
                SearchDecisionCounter counter = new(deck, size, budget);
                BigInteger actual = counter.Count(counter.Union(predicates));
                long expected = 0;
                for (int hand = 0; hand < (1 << physical.Length); hand++) {
                    if (BitOperations.PopCount((uint)hand) != size) {
                        continue;
                    }
                    bool success = predicates.OfType<CompiledEvent>().Any(predicate => predicate.Constraints.All(bound => {
                        int matches = physical.Where((row, copy) => (hand & (1 << copy)) != 0
                            && (bound.EligibleRows & (BigInteger.One << row)) != 0).Count();
                        return matches >= bound.MinCount && matches <= bound.MaxCount;
                    }));
                    if (success) {
                        expected++;
                    }
                }
                Assert.That(actual, Is.EqualTo(new BigInteger(expected)), $"trial {trial}, size {size}");
            }
        }
    }

    [Test]
    public void LargeMultiplicitiesUseExactIntegerWeightsAndChargeTheirPayload() {
        List<Card> deck = [new([], 100), new([], 100)];
        WorkBudget budget = new(CalculationWorkPolicy.Interactive);
        SearchDecisionCounter counter = new(deck, 100, budget);
        int root = counter.Union([new([new(1, 50, 50)])]);
        BigInteger choose = 1;
        for (int i = 1; i <= 50; i++) {
            choose = choose * (101 - i) / i;
        }
        Assert.That(counter.Count(root), Is.EqualTo(choose * choose));
        Assert.That(budget.PeakCells, Is.GreaterThan(WorkBudget.IntegerCells(choose * choose)));
        long spent = budget.Spent;
        WorkBudget insufficient = new(new(spent - 1));
        SearchDecisionCounter limited = new(deck, 100, insufficient);
        Assert.Throws<ProbabilityCalculationLimitException>(() => limited.Count(limited.Union([new([new(1, 50, 50)])])));
    }

    [Test]
    public void DecisionConstructionHonorsStandardStorageBoundary() {
        List<Card> deck = Enumerable.Range(0, 20).Select(_ => new Card([], 3)).ToList();
        Random random = new(713);
        List<CompiledEvent?> events = Enumerable.Range(0, 200).Select(_ => (CompiledEvent?)new CompiledEvent(
            Enumerable.Range(0, 6).Select(_ => new CountBound(random.Next(1 << 20), 2, 5)).ToArray())).ToList();
        WorkBudget budget = new(CalculationWorkPolicy.Interactive);
        SearchDecisionCounter counter = new(deck, 10, budget);
        ProbabilityCalculationLimitException? error = Assert.Throws<ProbabilityCalculationLimitException>(() => counter.Union(events));
        Assert.That(error!.Reason, Is.EqualTo(ProbabilityCalculationLimitReason.Storage));
        Assert.That(budget.PeakEntries, Is.LessThanOrEqualTo(CalculationStoragePolicy.Standard.EntryLimit + 1));
    }
}

public sealed partial class SearchEffectsTest {
    [Test]
    public void SeededThreeSourceAssignmentsAndAllHandSizesMatchIndependentOracle() {
        Random random = new(20415);
        for (int trial = 0; trial < 24; trial++) {
            List<Card> deck = [];
            for (int row = 0; row < 5; row++) {
                int membership = random.Next(4);
                Card card = new(new[] { A, B }.Where((_, bit) => (membership & (1 << bit)) != 0),
                    row == 0 ? 2 : 1, "same display name", id: $"entry-{row}");
                if (row < 3) {
                    CardSelector selector = trial % 3 == 0 ? CardSelector.AnyOf([A.Identity, B.Identity])
                        : CardSelector.For(row == 1 ? B : A);
                    card = card.WithSearch(new(selector, false)).WithDrawOncePerTurn(random.Next(2) == 0);
                }
                deck.Add(card);
            }
            List<Combo> combos = [
                new([new(A, 1, 1), new(B, 1, 6)], groupId: "g1"),
                new([new(A, 0, 0)], groupId: "g2", cards: [new("entry-3", 1, 6)]),
                new([ComboCategory.SameCard([A, B], 1, 2)], groupId: "g1"),
                new([], groupId: "g2", alternativeGroups: [new([
                    ComboAlternative.For(new ComboCard("entry-0", 1, 2)),
                    ComboAlternative.For(new ComboCategory(B, 2, 3))])])
            ];
            for (int size = 0; size <= 6; size++) {
                Compare(deck, combos, size);
            }
            if (trial % 4 == 0) {
                deck.Add(new([], id: "draw", drawCount: 1, preDrawRemovalCount: 1, preDrawSelector: CardSelector.For(A)));
                Compare(deck, combos, 2, new(DrawActivationMode.CustomPriority, ["draw"]));
            }
        }
    }

    [TestCase(0), TestCase(1), TestCase(3)]
    public async Task UpdatedMixedExamplePreservesEveryExactFractionUnderDefaultWork(int copies) {
        string[][] expected = [
            ["550033/658008", "271496/658008", "64470/658008", "150480/658008", "117819/658008", "16446/658008",
                "313972/658008", "23346/658008", "22091/658008", "402929/658008", "323132/658008"],
            ["4819127/5593068", "605063/1398267", "50344/466089", "2150321/8700328", "881/4403", "323839/11186136",
                "2826557/5593068", "25925/658008", "186041/4350164", "2649917/4121208", "20376263/39151476"],
            ["850650419/936887952", "3175343/6668658", "76252135/574221648", "156396941/539420336", "215997/866096",
                "126994447/3236522016", "8635987385/15257889504", "32999/669256", "14915003/215333118",
                "75445150015/106805226528", "3465516165/5933623696"]
        ];
        SessionState session = await LoadExample("search_example_session_state.json");
        if (copies > 0) {
            int index = session.Cards.FindIndex(card => card.Name == "Mulcharmy Fuwalos");
            Card original = session.Cards[index];
            session.Cards[index] = original.WithCopies(copies).WithDrawCount(2).WithDrawOncePerTurn(false);
            if (copies < original.Copies) {
                session.Cards.Add(original.Duplicate().WithCopies(original.Copies - copies));
            }
        }
        WorkBudget budget = new(CalculationWorkPolicy.Default);
        ExactCalculationResult result = new DrawEffectCalculation(session.Cards, 5, budget).Calculate(session.Combos, session.ComboGroups);
        ExactProbability[] actual = [result.Total, .. result.Combos, .. result.Groups];
        string[] reference = expected[copies == 3 ? 2 : copies];
        for (int index = 0; index < reference.Length; index++) {
            BigInteger[] fraction = reference[index].Split('/').Select(BigInteger.Parse).ToArray();
            AssertRatio(actual[index], fraction[0], fraction[1]);
        }
        Assert.That(budget.PeakCells, Is.LessThan(CalculationStoragePolicy.Standard.CellLimit));
        ExactCalculationResult only = new DrawEffectCalculation(session.Cards, 5, new(CalculationWorkPolicy.Default))
            .Calculate(session.Combos, session.ComboGroups, true);
        AssertRatio(only.Total, actual[0].Numerator, actual[0].Denominator);
        Assert.That(only.Combos, Is.Empty);
    }
}
