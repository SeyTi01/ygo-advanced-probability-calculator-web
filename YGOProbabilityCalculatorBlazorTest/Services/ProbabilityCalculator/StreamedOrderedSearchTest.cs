using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture, NonParallelizable]
public sealed class StreamedOrderedSearchTest {
    [TestCase("coalesced-uneven-targets")]
    [TestCase("minimal-filtered-target-discard")]
    [TestCase("mixed-visible-hidden")]
    [TestCase("maximum-only-removal")]
    [TestCase("or-and-filtered-removal")]
    [TestCase("draw-target-source-consumed")]
    [TestCase("multiple-draw-opt-duplicates")]
    [TestCase("solar-style-cost-and-mill")]
    [TestCase("grouped-level-hidden-True-target-source-True")]
    public void PrimaryWitnessAndStrongerBoundsMatchOldFastAndContainPhysicalTruth(string name) {
        SearchCertificateFixtures.Fixture fixture = SearchCertificateFixtures.Fixtures().Single(value => value.Name == name);
        SearchCertificateOracle.Counts truth = SearchCertificateOracle.Enumerate(fixture.Deck, fixture.Hand,
            fixture.Combos, fixture.Groups, fixture.Priority);
        foreach (bool merge in new[] { false, true }) {
            ExactCalculationResult[] results = AssertParity(fixture, merge);
            ExactProbability[] upper = Rows(results[0]);
            ExactProbability[] witness = Rows(results[1]);
            ExactProbability[] lower = Rows(results[2]);
            for (int row = 0; row < upper.Length; row++) {
                Assert.That(lower[row].Numerator * truth.Outcomes,
                    Is.LessThanOrEqualTo(truth.Wins[1][row] * lower[row].Denominator), name);
                Assert.That(upper[row].Numerator * truth.Outcomes,
                    Is.GreaterThanOrEqualTo(truth.Wins[1][row] * upper[row].Denominator), name);
                Assert.That(witness[row].Numerator * truth.Outcomes,
                    Is.EqualTo(truth.Wins[4][row] * witness[row].Denominator), name);
            }
        }
    }

    [TestCase(3, 40), TestCase(4, 40), TestCase(5, 40), TestCase(6, 40)]
    [TestCase(3, 60), TestCase(4, 60), TestCase(5, 60), TestCase(6, 60)]
    public void ScaledHandsKeepAllFractionsAndTerminalMass(int hand, int size) {
        SearchCertificateFixtures.Fixture fixture = Create(hand, size);
        AssertParity(fixture, true, expectStream: true);
    }

    [Test]
    public void TotalOnlyStillPreservesIndependentCertificateRows() {
        SearchCertificateFixtures.Fixture fixture = Create(3, 18);
        fixture.Combos.RemoveAt(fixture.Combos.Count - 1);
        ExactCalculationResult[] expected = AssertParity(fixture, true, expectStream: true);
        WorkBudget budget = new(CalculationWorkPolicy.Interactive);
        PostDrawCalculation calculation = new(fixture.Deck, fixture.Hand, budget, deferIdentities: false);
        ExactCalculationResult total = calculation.CalculateSearch(fixture.Combos, fixture.Groups, true, new(fixture.Deck, budget));
        Assert.That(total.Total.ToFraction(), Is.EqualTo(expected[0].Total.ToFraction()));
        Assert.That(total.Combos, Is.Empty);
        Assert.That(total.Groups, Is.Empty);
        AssertRows(calculation.CalculateSearchLower(fixture.Combos, fixture.Groups, true), expected[1], "Total-only witness");
        AssertRows(calculation.CalculateSearchLower(fixture.Combos, fixture.Groups, false), expected[2], "Total-only lower");
    }

    [TestCase("first-opt-priority", true)]
    [TestCase("two-copies", false)]
    [TestCase("two-sources", false)]
    [TestCase("cost", false)]
    [TestCase("depleted", true)]
    [TestCase("short-deck", false)]
    [TestCase("unavailable-discard", true)]
    [TestCase("discard-exceeds-hand", true)]
    [TestCase("minimum-too-high", false)]
    [TestCase("inactive-zero", true)]
    public void GuardKeepsPolicyBoundariesAndScalarCapacity(string variant, bool stream) {
        SearchCertificateFixtures.Fixture original = Create(3, 9);
        List<Card> cards = original.Deck;
        switch (variant) {
            case "first-opt-priority":
                cards[0] = new(cards[0].Categories, id: "draw", drawCount: 2, drawOncePerTurn: true,
                    postDrawDiscardCount: 1, postDrawDeckMillCount: 1, firstActivationOnly: true, blocksLaterDraws: true);
                break;
            case "two-copies":
                cards[0] = cards[0].WithCopies(2);
                break;
            case "two-sources":
                cards.Add(new([], id: "other", drawCount: 1));
                break;
            case "cost":
                cards[0] = cards[0].WithPreDrawRemovalCount(1);
                break;
            case "depleted":
            case "short-deck":
                cards[0] = cards[0].WithPostDrawDeckMillCount(variant == "depleted" ? 4 : 5);
                break;
            case "unavailable-discard":
                cards[0] = cards[0].WithPostDrawSelector(CardSelector.For(new CategoryBase("Absent")));
                break;
            case "discard-exceeds-hand":
                cards.Clear();
                cards.Add(new([], id: "draw", drawCount: 1, postDrawDiscardCount: 2, postDrawDeckMillCount: 1));
                cards.Add(new([], id: "search-a", search: new(CardSelector.Any, false)));
                cards.Add(new([], id: "filler"));
                original = original with { Hand = 1 };
                break;
            case "minimum-too-high":
                cards[0] = new(cards[0].Categories, id: "draw", drawCount: 2, postDrawDiscardCount: 1,
                    postDrawDeckMillCount: 1, minimumHandSize: 4);
                break;
            case "inactive-zero":
                cards.Add(new([], 2, active: false, id: "inactive", drawCount: 1));
                cards.Add(new([], 0, id: "zero", drawCount: 1));
                break;
        }
        AssertParity(original with { Priority = ["other", "draw"] }, true, stream);
    }

    [Test]
    public void FullNumericPredicateDisagreementCannotBePooledByTheStream() {
        CategoryBase monster = new("Monster", CategorySource.Metadata, "kind:monster");
        CategoryBase level = new("Level 8", CategorySource.Metadata, "level:8");
        List<Card> cards = [new([], id: "draw", drawCount: 2, postDrawDiscardCount: 1),
            new([], id: "search", search: new(CardSelector.Any, false)),
            new([monster, level], 2, id: "objective", externalCardId: 1),
            new([monster, level], 1, id: "manual", externalCardId: 2, manualMetadataCategoryKeys: ["level:8"]),
            new([], 2, id: "filler")];
        SearchCertificateFixtures.Fixture fixture = new("numeric guard", cards, 3,
            [new([new(new NumericPropertyRange(NumericPropertyKind.Level, 5, null, true).ToCategory(), 1, 3)], "Objective level")], []);
        ExactCalculationResult[] expected = AssertParity(fixture, true, expectStream: false);
        WorkBudget budget = new(CalculationWorkPolicy.Unlimited);
        PostDrawCalculation reused = new(cards, 3, budget, deferIdentities: false);
        // A new invocation must not reuse the previous streamed sums if its
        // predicates now require the historical identity expansion.
        reused.CalculateSearch([new([], "All completed branches")], [], false, new(cards, budget));
        AssertRows(reused.CalculateSearch(fixture.Combos, [], false, new(cards, budget)), expected[0], "Reused primary");
        AssertRows(reused.CalculateSearchLower(fixture.Combos, [], true), expected[1], "Reused witness");
        AssertRows(reused.CalculateSearchLower(fixture.Combos, [], false), expected[2], "Reused lower");
    }

    [Test]
    public void OptionalRefinementLimitRetainsCompletedPrimaryAndWitness() {
        SearchCertificateFixtures.Fixture fixture = Create(3, 18);
        // More copies than the conservative target-loss allowance forces the
        // second, optional source-present pass instead of the zero-target reuse.
        fixture.Deck[3] = fixture.Deck[3].WithCopies(5);
        WorkBudget measure = new(CalculationWorkPolicy.Unlimited);
        DrawEffectCalculation measured = new(fixture.Deck, fixture.Hand, measure);
        ExactCalculationResult expected = measured.Calculate(fixture.Combos, fixture.Groups);
        measured.DescribeCapability();
        ExactCalculationResult witness = measured.SearchExecution!.CalculateLower(fixture.Combos, fixture.Groups, true);
        long completed = measure.Spent;
        bool limited = false;
        for (long allowance = completed + 1000; allowance < completed + 10_000; allowance += 1000) {
            WorkBudget budget = new(new(allowance));
            DrawEffectCalculation calculation = new(fixture.Deck, fixture.Hand, budget);
            ExactCalculationResult actual = calculation.Calculate(fixture.Combos, fixture.Groups);
            SearchAvailabilityCertificate certificate = SearchAvailabilityCertification.TryCalculate(fixture.Combos,
                fixture.Groups, actual, budget, calculation);
            AssertRows(actual, expected, "Completed primary");
            if (certificate.Detail.Contains("Limited refinement") && certificate.Total?.Lower.Numerator != "0"
                && certificate.Total is not null) {
                Assert.That(certificate.Total.Lower, Is.EqualTo(witness.Total.ToFraction()));
                Assert.That(certificate.Total.Upper, Is.EqualTo(expected.Total.ToFraction()));
                Assert.That(certificate.Combos.Select(row => row.Lower), Is.EqualTo(witness.Combos.Select(value => value.ToFraction())));
                Assert.That(certificate.Combos.Select(row => row.Upper), Is.EqualTo(expected.Combos.Select(value => value.ToFraction())));
                Assert.That(certificate.Groups.Select(row => row.Lower), Is.EqualTo(witness.Groups.Select(value => value.ToFraction())));
                Assert.That(certificate.Groups.Select(row => row.Upper), Is.EqualTo(expected.Groups.Select(value => value.ToFraction())));
                Assert.That(certificate.Status, Is.EqualTo(SearchAvailabilityStatus.Bounded));
                limited = true;
                break;
            }
        }
        Assert.That(limited, Is.True);
    }

    private static SearchCertificateFixtures.Fixture Create(int hand, int size) {
        CategoryBase first = new("First");
        CategoryBase second = new("Second");
        List<Card> cards = [new([first], id: "draw", drawCount: 2, postDrawDiscardCount: 1, postDrawDeckMillCount: 1),
            new([], id: "search-a", search: new(CardSelector.For(first), false)),
            new([first], id: "search-b", search: new(CardSelector.For(second), false), oncePerTurn: true),
            new([first, second], 2, "alias", id: "both"), new([first], 1, "alias", id: "first"),
            new([second], 1, id: "second"), new([], size - 7, id: "filler")];
        List<Combo> combos = [new([new(first, 1, hand), new(second, 1, hand)], "Distinct", groupId: "g"),
            new([ComboCategory.SameCard([first, second], 1, hand)], "Same card", groupId: "g"),
            new([new(first, 0, 0)], "Exclude first"),
            new([], "Direct and zero", cards: [new("first", 1, hand), new("both", 0, 0)], groupId: "g"),
            new([], "Optional and fetched sources", cards: [new("search-b", 1, hand)]),
            new([], "Completed")];
        return new("scaled", cards, hand, combos, [new("g", "Grouped"), new("empty", "Empty")]);
    }

    private static ExactCalculationResult[] AssertParity(SearchCertificateFixtures.Fixture fixture, bool merge, bool? expectStream = null) {
        List<Card> cards = fixture.Deck.Where(card => card.Active && card.Copies > 0).ToList();
        Dictionary<string, BigInteger>[] terminal = [new(), new()];
        ExactCalculationResult[][] results = new ExactCalculationResult[2][];
        for (int side = 0; side < 2; side++) {
            int captured = side;
            SearchCountingDiagnostics diagnostics = new();
            WorkBudget budget = new(CalculationWorkPolicy.Unlimited, CalculationStoragePolicy.VeryHigh) { SearchDiagnostics = diagnostics };
            DrawActivationPriority? priority = fixture.Priority is null ? null : new(DrawActivationMode.CustomPriority, fixture.Priority);
            PostDrawCalculation calculation = new(cards, fixture.Hand, budget, priority, deferIdentities: false) {
                StreamSearchTerminals = side == 1,
                ObserveTerminal = (hand, mass) => {
                    string key = string.Join(',', hand);
                    terminal[captured][key] = terminal[captured].GetValueOrDefault(key) + mass;
                    Array.Fill(hand, int.MinValue);
                }
            };
            results[side] = [calculation.CalculateSearch(fixture.Combos, fixture.Groups, false, new(cards, budget), coalesceOrdinary: merge),
                calculation.CalculateSearchLower(fixture.Combos, fixture.Groups, true),
                calculation.CalculateSearchLower(fixture.Combos, fixture.Groups, false),
                calculation.CalculateSearchLower(fixture.Combos, fixture.Groups, false, useKnownLoss: false)];
            if (side == 1 && expectStream is bool expected) {
                Assert.That(diagnostics.Counts.ContainsKey("primary/streamed search terminals"), Is.EqualTo(expected), fixture.Name);
            }
        }
        Assert.That(terminal[1], Is.EquivalentTo(terminal[0]), fixture.Name + " complete terminal mass");
        for (int pass = 0; pass < results[0].Length; pass++) {
            AssertRows(results[1][pass], results[0][pass], fixture.Name + "/pass=" + pass);
        }
        return results[1];
    }

    private static void AssertRows(ExactCalculationResult actual, ExactCalculationResult expected, string message) {
        Assert.That(Rows(actual).Select(value => value.ToFraction()), Is.EqualTo(Rows(expected).Select(value => value.ToFraction())), message);
    }

    private static ExactProbability[] Rows(ExactCalculationResult result) => [result.Total, .. result.Combos, .. result.Groups];
}
