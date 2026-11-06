using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.CalculationEngineAdapter;
using CP = CardProbability;
using YGOProbabilityCalculatorBlazorTest.Fixtures;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

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
        SessionState session = await ExampleSessionFixture.LoadAsync("search_example_session_state.json");
        if (copies > 0) {
            int index = session.Cards.FindIndex(card => card.Name == "Mulcharmy Fuwalos");
            Card original = session.Cards[index];
            session.Cards[index] = original.WithCopies(copies).WithDrawCount(2).WithDrawOncePerTurn(false);
            if (copies < original.Copies) {
                session.Cards.Add(original.Duplicate().WithCopies(original.Copies - copies));
            }
        }
        CP.CalculationRequest request = new CalculationRequestAdapter(session.Cards, true).Create(session.Combos, 5, session.ComboGroups);
        CP.CalculationOptions options = new(CP.CalculationMode.OptimisticSearchProjection,
            new CP.ExecutionPolicy(CalculationWorkPolicy.Default.WorkUnits));
        CP.CalculationResult result = CP.CalculationEngine.Calculate(request, options);
        Assert.That(result.Status, Is.EqualTo(CP.CalculationStatus.Completed));
        CP.RationalProbability[] actual = [result.Total!.Primary, .. result.Goals.Select(goal => goal.Value.Primary), .. result.Groups.Select(group => group.Value.Primary)];
        string[] reference = expected[copies == 3 ? 2 : copies];
        for (int index = 0; index < reference.Length; index++) {
            BigInteger[] fraction = reference[index].Split('/').Select(BigInteger.Parse).ToArray();
            Assert.That(BigInteger.Parse(actual[index].Numerator) * fraction[1],
                Is.EqualTo(fraction[0] * BigInteger.Parse(actual[index].Denominator)));
        }
        Assert.That(result.Metrics.PeakCells, Is.LessThan(CalculationStoragePolicy.Standard.CellLimit));
        CP.CalculationResult only = CP.CalculationEngine.CalculateUnion(request, options);
        Assert.That(only.Status, Is.EqualTo(CP.CalculationStatus.Completed));
        Assert.That(only.Total!.Primary, Is.EqualTo(actual[0]));
        Assert.That(only.Goals, Is.Empty);
    }
}
