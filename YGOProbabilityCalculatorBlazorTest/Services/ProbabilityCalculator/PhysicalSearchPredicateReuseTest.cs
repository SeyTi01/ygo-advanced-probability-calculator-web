using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public sealed class PhysicalSearchPredicateReuseTest {
    [Test]
    public void RepeatedPublicOpeningCalculationsPreserveEveryLabeledHandAndNormalizedRequirement() {
        CategoryBase a = new("A");
        CategoryBase b = new("B");
        List<Card> cards = [new([a, b], 2, "Same name", id: "ab", externalCardId: 123),
            new([a], 2, "Same name", id: "a", externalCardId: 123), new([b], id: "b"), new([], id: "f")];
        ComboAlternativeGroup alternatives = new([
            ComboAlternative.For(new ComboCategory(b, 2, 0, RequirementMaximumMode.HandSize)),
            ComboAlternative.For(new ComboCard("f", 1, 1))
        ]);
        List<Combo> combos = [
            new([], "Empty conjunction"),
            new([new(a, 0, 0)], "Zero maximum"),
            new([new(a, 0, 2)], "Initially vacuous fixed maximum"),
            new([new(a, 3, 0, RequirementMaximumMode.HandSize)], "Initially impossible dynamic maximum"),
            new([new(a, 1, 6), new(b, 1, 6)], "Distinct overlapping roles"),
            new([ComboCategory.SameCard([a, b], 1, 0, RequirementMaximumMode.HandSize)], "Same-card conjunction"),
            new([new(a, 1, 6)], "Direct and category need separate copies", cards: [new("ab", 1, 6)]),
            new([], "Distinct entry IDs", cards: [new("ab", 1, 6), new("a", 0, 0)]),
            new([new(a, 1, 6), new(a, 2, 2)], "Repeated category normalization"),
            new([], "Repeated direct normalization", cards: [new("ab", 1, 6), new("ab", 2, 2)]),
            new([new(a, 1, 0, RequirementMaximumMode.HandSize)], "Repeated OR with shared conjunction",
                alternativeGroups: [alternatives, alternatives])
        ];
        int[] labels = cards.SelectMany((card, row) => Enumerable.Repeat(row, card.Copies)).ToArray();
        foreach (int size in new[] { 0, 2, 1, 4, 3, 0, 6, 2 }) {
            for (int subset = 0; subset < 1 << labels.Length; subset++) {
                if (BitOperations.PopCount((uint)subset) != size) {
                    continue;
                }
                int[] counts = new int[cards.Count];
                List<Card> hand = [];
                for (int label = 0; label < labels.Length; label++) {
                    if ((subset & 1 << label) != 0) {
                        counts[labels[label]]++;
                        hand.Add(cards[labels[label]]);
                    }
                }
                bool[] expected = combos.Select(combo => SmallDeckOracle.MatchesHand(hand, combo)).ToArray();
                ProbabilityCalculationResult result = CalculateOpening(cards, counts, combos);
                Assert.That(result.ComboProbabilities.Select(row => row.Probability), Is.EqualTo(expected.Select(value => value ? 1.0 : 0.0)),
                    $"size={size}, labeled subset={subset}");
                Assert.That(result.TotalProbability, Is.EqualTo(expected.Any(value => value) ? 1.0 : 0.0));
            }
        }
    }

    [Test]
    public void NumericAndSameCardSelectorsKeepTheirMeaningForEveryPhysicalHand() {
        CategoryBase role = new("Role");
        CategoryBase monster = new("Monster", CategorySource.Metadata, "kind:monster");
        CategoryBase range = new NumericPropertyRange(NumericPropertyKind.Level, 3, 5).ToCategory();
        List<Card> cards = [
            new([monster, new("Level 3", CategorySource.Metadata, "level:3")], id: "level-3", externalCardId: 1),
            new([monster, new("Level 4", CategorySource.Metadata, "level:4"), role], id: "level-4", externalCardId: 2),
            new([monster, new("Level 6", CategorySource.Metadata, "level:6"), role], id: "level-6", externalCardId: 3),
            new([monster, new("Rank 4", CategorySource.Metadata, "rank:4")], id: "rank-4", externalCardId: 4)
        ];
        List<Combo> combos = [new([new(range, 1, 0, RequirementMaximumMode.HandSize)]), new([new(range, 0, 0)]),
            new([new(range, 1, 2)], cards: [new("level-3", 1, 4)]),
            new([ComboCategory.SameCard([range, role], 1, 4)])];
        foreach (int subset in Enumerable.Range(0, 16).Reverse()) {
            int[] hand = Enumerable.Range(0, cards.Count).Select(row => (subset >> row) & 1).ToArray();
            // Only labels 0 and 1 are Level 3..5. Label 1 alone also has Role;
            // the direct Level-3 requirement must use a different physical copy.
            bool[] expected = [hand[0] + hand[1] > 0, hand[0] + hand[1] == 0,
                hand[0] == 1 && hand[1] == 1, hand[1] == 1];
            ProbabilityCalculationResult result = CalculateOpening(cards, hand, combos);
            Assert.That(result.ComboProbabilities.Select(row => row.Probability), Is.EqualTo(expected.Select(value => value ? 1.0 : 0.0)),
                "labeled subset=" + subset);
        }
    }

    [TestCase(false), TestCase(true)]
    public void MixedRepeatedDrawsPaymentsAndTerminalSearchMatchIndependentPhysicalOrders(bool hiddenLoss) {
        CategoryBase a = new("A");
        CategoryBase b = new("B");
        List<Card> cards = [new([], id: "d", drawCount: 2, postDrawDiscardCount: 1, postDrawSelector: CardSelector.For(b)),
            new([], 2, id: "e", drawCount: 1, oncePerTurn: false, activationLimit: 2,
                preDrawRemovalCount: 1, preDrawSelector: CardSelector.For(a), preDrawDeckRemovalCount: hiddenLoss ? 1 : 0),
            new([], id: "s", search: new(CardSelector.AnyOfSelectors([CardSelector.For(a), CardSelector.For(b)]), false)),
            new([a, b], id: "ab"), new([a], id: "a"), new([b], id: "b")];
        List<Combo> combos = [new([new(a, 1, 3), new(b, 1, 3)], groupId: "g"),
            new([new(a, 0, 0), new(b, 1, 3)], groupId: "g"),
            new([], cards: [new("e", 1, 0, RequirementMaximumMode.HandSize), new("s", 0, 0)]),
            new([new(a, 1, 3)], cards: [new("ab", 1, 3)]), new([], "Completed branch", groupId: "complete")];
        List<ComboGroup> groups = [new("g", "Union"), new("complete", "Complete"), new("empty", "Empty")];
        DrawActivationPriority priority = new(DrawActivationMode.CustomPriority, ["e", "s", "d"]);
        TwoPhaseSearchOracle.Rational[] oracle = TwoPhaseSearchOracle.Count(cards, 2, combos, groups, priority);
        // The empty conjunction records every completed branch. Filtered mandatory
        // discards can fail, and those labels must remain in the original sample space.
        Assert.That(oracle[5].Numerator, Is.GreaterThan(BigInteger.Zero));
        Assert.That(oracle[5].Numerator, Is.LessThan(oracle[5].Denominator));
        ProbabilityCalculationResult result = new ProbabilityCalculatorService().CalculateProbabilityResults(
            cards, combos, 2, groups, CalculationWorkPolicy.Unlimited, new(priority, Mode: CalculationModel.Exact));
        Assert.That(result.Contract!.Accuracy, Is.EqualTo(CalculationAccuracy.Exact));
        ApplicationResultFractions actual = ApplicationResultFractions.From(result);
        ApplicationFraction[] rows = [actual.Total, .. actual.Combos, .. actual.Groups];
        Assert.That(rows, Has.Length.EqualTo(oracle.Length));
        for (int row = 0; row < rows.Length; row++) {
            Assert.That(rows[row].Numerator * oracle[row].Denominator,
                Is.EqualTo(oracle[row].Numerator * rows[row].Denominator), "row=" + row);
        }
    }

    private static ProbabilityCalculationResult CalculateOpening(List<Card> cards, int[] counts, List<Combo> combos) {
        List<Card> hand = cards.Select((card, index) => card.WithCopies(counts[index])).Where(card => card.Copies > 0).ToList();
        ProbabilityCalculationResult result = new ProbabilityCalculatorService().CalculateProbabilityResults(
            hand, combos, counts.Sum(), [], CalculationWorkPolicy.Unlimited, new(Mode: CalculationModel.OpeningHand));
        Assert.That(result.Contract!.Accuracy, Is.EqualTo(CalculationAccuracy.Exact));

        return result;
    }
}
