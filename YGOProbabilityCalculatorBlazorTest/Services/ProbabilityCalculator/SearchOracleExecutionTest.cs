using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture, NonParallelizable]
public sealed class SearchOracleExecutionTest {
    [TestCase("draw", false), TestCase("draw", true)]
    [TestCase("cost", false), TestCase("cost", true)]
    [TestCase("discard", false), TestCase("discard", true)]
    [TestCase("hidden", false), TestCase("hidden", true)]
    public void AccessWeightsAndReusedPredicatesMatchOriginalLabeledExecution(string mode, bool opt) {
        (List<Card> deck, List<Combo> combos, List<ComboGroup> groups) = Fixture(mode, opt);
        string[]? priority = opt ? ["draw", "source"] : null;
        (BigInteger total, BigInteger[] wins) = SearchAccessOracle.Count(deck, combos, 2, groups, priority,
            collapseIdenticalCopies: false, reuseHandPredicates: false);
        Assert.That(total, Is.EqualTo(new BigInteger(720)));
        foreach ((bool collapse, bool reuse) in new[] { (true, false), (false, true), (true, true) }) {
            (BigInteger actualTotal, BigInteger[] actualWins) = SearchAccessOracle.Count(deck, combos, 2, groups, priority,
                collapseIdenticalCopies: collapse, reuseHandPredicates: reuse);
            Assert.That(actualTotal, Is.EqualTo(total), $"collapse={collapse}, reuse={reuse}");
            Assert.That(actualWins, Is.EqualTo(wins), $"collapse={collapse}, reuse={reuse}");
        }
    }

    [TestCase("draw", false), TestCase("draw", true)]
    [TestCase("cost", false), TestCase("cost", true)]
    [TestCase("discard", false), TestCase("discard", true)]
    [TestCase("hidden", false), TestCase("hidden", true)]
    public void PhysicalWeightsPreserveEveryCertificateModeAndTerminalCheck(string mode, bool opt) {
        (List<Card> deck, List<Combo> combos, List<ComboGroup> groups) = Fixture(mode, opt);
        string[]? priority = opt ? ["draw", "source"] : null;
        List<string>[] terminals = [new(), new()];
        SearchCertificateOracle.Counts[] results = new SearchCertificateOracle.Counts[2];
        for (int path = 0; path < 2; path++) {
            int side = path;
            results[path] = SearchCertificateOracle.Enumerate(deck, 2, combos, groups, priority,
                (hand, remaining, lost, lower) => terminals[side].Add(
                    $"{string.Join(',', hand.Select(card => card.Id).Order(StringComparer.Ordinal))}|{remaining}|{lost}|{string.Join(',', lower)}"),
                collapseIdenticalCopies: path == 1);
        }
        Assert.Multiple(() => {
            Assert.That(results[0].Outcomes, Is.EqualTo(new BigInteger(720)));
            Assert.That(results[1].Outcomes, Is.EqualTo(results[0].Outcomes));
            Assert.That(results[1].Failed, Is.EqualTo(results[0].Failed));
            Assert.That(results[1].Wins, Is.EqualTo(results[0].Wins), "All five modes, including every combo, group and total.");
            Assert.That(results[1].ResolvedStates, Is.EqualTo(results[0].ResolvedStates));
            Assert.That(results[1].SearchCalls, Is.EqualTo(results[0].SearchCalls));
            Assert.That(results[1].LossAllocations, Is.EqualTo(results[0].LossAllocations));
            Assert.That(terminals[1], Is.EquivalentTo(terminals[0]), "Every callback, including repeated held hands with different physical suffixes.");
        });
        if (mode == "discard") {
            Assert.That(results[1].Failed, Is.GreaterThan(BigInteger.Zero));
        }
    }

    private static (List<Card> Deck, List<Combo> Combos, List<ComboGroup> Groups) Fixture(string mode, bool opt) {
        CategoryBase a = new("A");
        CategoryBase b = new("B");
        // Same display name across distinct source/target/effect entries must
        // never identify copies. Duplicate sources and draws exercise OPT and
        // physical encounter ties; both loss operations retain hidden positions.
        List<Card> deck = [
            new([b], mode == "hidden" ? 1 : 2, "same", id: "source", search: new(CardSelector.For(a), false), oncePerTurn: opt),
            new([a, b], 2, "same", id: "target"),
            new([], mode == "hidden" ? 2 : 1, "same", id: "draw", drawCount: 1, drawOncePerTurn: opt,
                preDrawRemovalCount: mode == "cost" ? 1 : 0, preDrawSelector: mode == "cost" ? CardSelector.For(a) : null,
                postDrawDiscardCount: mode == "discard" ? 1 : 0, postDrawSelector: mode == "discard" ? CardSelector.For(a) : null,
                preDrawDeckRemovalCount: mode == "hidden" ? 1 : 0, postDrawDeckMillCount: mode == "hidden" ? 1 : 0),
            new([], 1, "same", id: "blank")
        ];
        List<Combo> combos = [
            new([new(a, 1, 1), new(b, 1, 3)], groupId: "g"),
            new([new(a, 0, 0)], groupId: "g"),
            new([ComboCategory.SameCard([a, b], 1, 0, RequirementMaximumMode.HandSize)], groupId: "h"),
            new([], groupId: "h", alternativeGroups: [new([
                ComboAlternative.For(new ComboCard("source", 1, 0, RequirementMaximumMode.HandSize)),
                ComboAlternative.For(new ComboCard("target", 2, 2))])])
        ];

        return (deck, combos, [new("g", "Bounds and distinct copies"), new("h", "Alternatives"), new("empty", "Empty")]);
    }
}
