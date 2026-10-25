using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Counting;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.DrawResolution;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Search;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture, NonParallelizable]
public sealed class MultiCopyOrderedAdversarialTest {
    [TestCase("searchable-source-no-removal", true)]
    [TestCase("full-depletion", true)]
    [TestCase("selective-failure", true)]
    [TestCase("hidden-source-copies", true)]
    [TestCase("saved-limit-one", true)]
    [TestCase("only-source-copies", true)]
    [TestCase("repeatable-copies", false)]
    [TestCase("saved-limit-two", false)]
    [TestCase("short-deck", false)]
    public void LabeledPhysicalOrdersChallengeSourceMultiplicityAndJointSearch(string boundary, bool aggregated) {
        CategoryBase first = new("First");
        CategoryBase second = new("Second");
        bool limitOne = boundary == "saved-limit-one";
        bool repeatable = boundary is "repeatable-copies" or "saved-limit-two";
        int sourceCopies = limitOne || boundary == "only-source-copies" ? 3 : 2;
        int hiddenBefore = boundary is "hidden-source-copies" or "short-deck" ? 1 : 0;
        int hiddenAfter = boundary is "hidden-source-copies" or "full-depletion" or "short-deck" ? 1 : 0;
        int removal = boundary == "selective-failure" ? 2 : boundary == "hidden-source-copies" ? 1 : 0;
        int hand = boundary switch {
            "only-source-copies" => 1,
            "full-depletion" => 4,
            "short-deck" => 5,
            _ => 2
        };
        Card source = new([first], sourceCopies, id: "draw", drawCount: 2,
            drawOncePerTurn: !limitOne && !repeatable,
            activationLimit: limitOne ? 1 : boundary == "saved-limit-two" ? 2 : null,
            preDrawDeckRemovalCount: hiddenBefore, postDrawDeckMillCount: hiddenAfter,
            postDrawDiscardCount: removal,
            postDrawSelector: boundary == "selective-failure" ? CardSelector.For(second) : CardSelector.Any);
        List<Card> cards = [source,
            new([], id: "search", search: new(CardSelector.Any, false)),
            new([first], id: "first"), new([second], id: "second"),
            new([first, second], id: "both"), new([], id: "inert")];
        if (boundary == "only-source-copies") {
            cards = [source];
        }
        List<Combo> combos = [
            new([], "No source", cards: [new("draw", 0, 0)], groupId: "source"),
            new([], "One source", cards: [new("draw", 1, 1)], groupId: "source"),
            new([], "Two sources", cards: [new("draw", 2, 2)], groupId: "source"),
            new([new(first, 1, 2), new(second, 1, 2)], "Distinct roles", groupId: "roles"),
            new([new(second, 0, 0)], "Exclude second", groupId: "roles"),
            new([], "Fetched searchers remain inert", cards: [new("search", 1, 1), new("both", 1, 1)])
        ];
        List<ComboGroup> groups = [new("source", "Source bounds"), new("roles", "Role bounds"), new("empty", "Empty")];
        List<Card> referenceCards = cards.ToList();
        if (limitOne) {
            // The independent scheduler tracks OPT rather than saved limits.
            // With one source entry, replacing explicit limit one by OPT changes
            // no legal activation or removal decision: both leave allowance one.
            // This deck has room for repeat activations absent that restriction.
            referenceCards[0] = source.WithOncePerTurn(true);
        }
        SearchCertificateOracle.Counts truth = SearchCertificateOracle.Enumerate(referenceCards, hand, combos, groups,
            collapseIdenticalCopies: false);

        SearchCountingDiagnostics diagnostics = new();
        WorkBudget exactBudget = new(CalculationWorkPolicy.Unlimited) { SearchDiagnostics = diagnostics };
        BigInteger completedMass = BigInteger.Zero;
        PostDrawCalculation exact = new(cards, hand, exactBudget, deferIdentities: false) {
            ObservePhysicalOutcome = (_, _, _, mass) => completedMass += mass
        };
        ExactCalculationResult physical = exact.CalculateSearch(combos, groups, false, new(cards, exactBudget),
            physical: true, method: PhysicalEnumerationMethod.SingleSource, coalesceOrdinary: false);
        ExactProbability[] physicalRows = Rows(physical);
        for (int row = 0; row < physicalRows.Length; row++) {
            Assert.That(physicalRows[row].Numerator * truth.Outcomes,
                Is.EqualTo(truth.Wins[1][row] * physicalRows[row].Denominator), boundary + "/physical/" + row);
        }
        Assert.That(completedMass + truth.Failed, Is.EqualTo(truth.Outcomes), boundary + "/complete sample space");
        Assert.That(diagnostics.Counts.GetValueOrDefault("primary/physical single-source frames") > 0, Is.EqualTo(aggregated));
        if (boundary == "selective-failure") {
            Assert.That(truth.Failed, Is.GreaterThan(BigInteger.Zero));
        }

        ExactCalculationResult[][] results = new ExactCalculationResult[2][];
        for (int path = 0; path < results.Length; path++) {
            WorkBudget budget = new(CalculationWorkPolicy.Unlimited);
            PostDrawCalculation calculation = new(cards, hand, budget, deferIdentities: false) {
                StreamSearchTerminals = path == 1
            };
            results[path] = [calculation.CalculateSearch(combos, groups, false, new(cards, budget)),
                calculation.CalculateSearchLower(combos, groups, true),
                calculation.CalculateSearchLower(combos, groups, false)];
        }
        for (int pass = 0; pass < results[0].Length; pass++) {
            Assert.That(Rows(results[1][pass]).Select(value => value.ToFraction()),
                Is.EqualTo(Rows(results[0][pass]).Select(value => value.ToFraction())), boundary + "/pass/" + pass);
        }
        for (int row = 0; row < physicalRows.Length; row++) {
            ExactProbability upper = Rows(results[1][0])[row];
            ExactProbability witness = Rows(results[1][1])[row];
            ExactProbability lower = Rows(results[1][2])[row];
            Assert.That(upper.Numerator * truth.Outcomes,
                Is.GreaterThanOrEqualTo(truth.Wins[1][row] * upper.Denominator), boundary + "/upper/" + row);
            Assert.That(witness.Numerator * truth.Outcomes,
                Is.EqualTo(truth.Wins[4][row] * witness.Denominator), boundary + "/witness/" + row);
            Assert.That(lower.Numerator * truth.Outcomes,
                Is.LessThanOrEqualTo(truth.Wins[1][row] * lower.Denominator), boundary + "/lower/" + row);
        }
    }

    private static ExactProbability[] Rows(ExactCalculationResult result) => [result.Total, .. result.Combos, .. result.Groups];
}
