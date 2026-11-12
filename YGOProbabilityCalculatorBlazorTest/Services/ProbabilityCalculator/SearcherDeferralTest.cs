using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.CalculationEngineAdapter;
using CP = CardProbability;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture, NonParallelizable]
public sealed class SearcherDeferralTest {
    [Test]
    public void OrderedApplicationFixturesMatchChronologicalOracleThroughPublicApi() {
        foreach (SearchCertificateFixtures.Fixture fixture in SearchCertificateFixtures.OrderedCoverage()) {
            SearchCertificateOracle.Counts oracle = SearchCertificateOracle.Enumerate(fixture.Deck, fixture.Hand, fixture.Combos, fixture.Groups, fixture.Priority);
            DrawActivationPriority? priority = fixture.Priority is null ? null : new(DrawActivationMode.CustomPriority, fixture.Priority);
            AssertOracle(Calculate(fixture.Deck, fixture.Hand, fixture.Combos, fixture.Groups, priority, CP.CalculationMode.Exact), oracle, 1);
            AssertOracle(Calculate(fixture.Deck, fixture.Hand, fixture.Combos, fixture.Groups, priority, CP.CalculationMode.OptimisticSearchProjection), oracle, 2);
        }
    }

    [TestCase(0), TestCase(1), TestCase(2), TestCase(6)]
    public void RemovableSearcherObservationsAndPoolBoundaries(int hand) {
        CategoryBase material = new("Material");
        CategoryBase target = new("Target");
        List<Card> cards = [
            new([], 1, id: "draw", drawCount: 1, postDrawDiscardCount: 1, postDrawSelector: CardSelector.For(material)),
            new([material], 1, id: "removable", search: new(CardSelector.For(target), false)),
            new([target], 1, id: "observed-search", search: new(CardSelector.For(target), false)),
            new([], 1, id: "unobserved-search", search: new(CardSelector.For(target), false)),
            new([target], 1, id: "target"), new([], 1, id: "filler")
        ];
        List<Combo> combos = [new([new(target, 1, 2)]), new([], cards: [new("observed-search", 0, 0)]),
            new([], cards: [new("unobserved-search", 1, 1)])];
        SearchCertificateOracle.Counts oracle = SearchCertificateOracle.Enumerate(cards, hand, combos, []);
        AssertOracle(Calculate(cards, hand, combos, [], null, CP.CalculationMode.Exact), oracle, 1);
        AssertOracle(Calculate(cards, hand, combos, [], null, CP.CalculationMode.OptimisticSearchProjection), oracle, 2);
    }

    [Test]
    public async Task OriginalSessionPreservesProjectedAndPhysicalFractions() {
        SessionState state = await SelectiveSurvivorTest.Source();
        CP.CalculationResult projection = Calculate(state.Cards, state.HandSize, state.Combos, state.ComboGroups,
            state.DrawActivationPriority, CP.CalculationMode.OptimisticSearchProjection);
        Assert.That(projection.Total!.Primary, Is.EqualTo(new CP.RationalProbability("63931285207", "68906597760")));
        CP.CalculationResult exact = Calculate(state.Cards, state.HandSize, state.Combos, state.ComboGroups,
            state.DrawActivationPriority, CP.CalculationMode.Exact);
        Assert.That(exact.Total!.Primary, Is.EqualTo(new CP.RationalProbability("404499773243054219", "436593827634307200")));
        ApplicationFraction historicalLower = new(BigInteger.Parse("61422732523"), BigInteger.Parse("68906597760"));
        ApplicationFraction truth = ApplicationFraction.From(exact.Total.Primary);
        Assert.That(historicalLower.Numerator * truth.Denominator, Is.LessThanOrEqualTo(truth.Numerator * historicalLower.Denominator));
        Assert.That(projection.Total.Certified, Is.Not.Null);
        ApplicationFraction certifiedLower = ApplicationFraction.From(projection.Total.Certified!.Lower);
        Assert.That(certifiedLower.Numerator * historicalLower.Denominator,
            Is.GreaterThanOrEqualTo(historicalLower.Numerator * certifiedLower.Denominator));
    }

    private static CP.CalculationResult Calculate(
        List<Card> cards,
        int hand,
        List<Combo> combos,
        IReadOnlyList<ComboGroup> groups,
        DrawActivationPriority? priority,
        CP.CalculationMode mode
    ) {
        CP.CalculationRequest request = new CalculationRequestAdapter(cards, true).Create(combos, hand, groups);
        HashSet<string> ids = request.Entries.Select(entry => entry.Id).ToHashSet(StringComparer.Ordinal);
        CP.CalculationOptions options = new(mode, new(CalculationWorkPolicy.Interactive.WorkUnits),
            priority?.Mode == DrawActivationMode.CustomPriority ? CP.ActivationOrder.Priority : CP.ActivationOrder.EncounterOrder,
            priority?.CardIds.Where(ids.Contains));
        CP.CalculationResult result = CP.CalculationEngine.Calculate(request, options);
        Assert.That(result.Status, Is.EqualTo(CP.CalculationStatus.Completed), result.Detail);
        return result;
    }

    private static void AssertOracle(CP.CalculationResult result, SearchCertificateOracle.Counts oracle, int stage) {
        CP.RationalProbability[] rows = [result.Total!.Primary, .. result.Goals.Select(goal => goal.Value.Primary), .. result.Groups.Select(group => group.Value.Primary)];
        for (int row = 0; row < rows.Length; row++) {
            Assert.That(BigInteger.Parse(rows[row].Numerator) * oracle.Outcomes,
                Is.EqualTo(oracle.Wins[stage][row] * BigInteger.Parse(rows[row].Denominator)), "row " + row);
        }
    }
}
