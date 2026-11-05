using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture, NonParallelizable]
public sealed class MultiCopyOrderedAdversarialTest {
    [TestCase("searchable-source-no-removal")]
    [TestCase("full-depletion")]
    [TestCase("selective-failure")]
    [TestCase("hidden-source-copies")]
    [TestCase("saved-limit-one")]
    [TestCase("only-source-copies")]
    [TestCase("repeatable-copies")]
    [TestCase("saved-limit-two")]
    [TestCase("short-deck")]
    public void LabeledPhysicalOrdersChallengeSourceMultiplicityAndJointSearch(string boundary) {
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

        ProbabilityCalculatorService service = new();
        ProbabilityCalculationResult exact = service.CalculateProbabilityResults(cards, combos, hand, groups,
            CalculationWorkPolicy.Unlimited, new(Mode: CalculationModel.Exact));
        ApplicationFraction[] physical = Rows(ApplicationResultFractions.From(exact));
        for (int row = 0; row < physical.Length; row++) {
            Assert.That(physical[row].Numerator * truth.Outcomes,
                Is.EqualTo(truth.Wins[1][row] * physical[row].Denominator), boundary + "/physical/" + row);
        }
        Assert.That(exact.Contract!.Accuracy, Is.EqualTo(CalculationAccuracy.Exact));
        if (boundary == "selective-failure") {
            Assert.That(truth.Failed, Is.GreaterThan(BigInteger.Zero));
        }
        ProbabilityCalculationResult projected = service.CalculateProbabilityResults(cards, combos, hand, groups,
            CalculationWorkPolicy.Unlimited, new(CertifySearchAvailability: true));
        ApplicationFraction[] upper = Rows(ApplicationResultFractions.From(projected));
        for (int row = 0; row < upper.Length; row++) {
            Assert.That(upper[row].Numerator * truth.Outcomes,
                Is.EqualTo(truth.Wins[2][row] * upper[row].Denominator), boundary + "/optimistic/" + row);
        }
    }

    private static ApplicationFraction[] Rows(ApplicationResultFractions result) => [result.Total, .. result.Combos, .. result.Groups];
}
