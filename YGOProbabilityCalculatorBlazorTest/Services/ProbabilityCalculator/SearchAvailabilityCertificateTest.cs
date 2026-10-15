using System.Numerics;
using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.Session;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture, NonParallelizable]
public sealed class SearchAvailabilityCertificateTest {
    public static IEnumerable<TestCaseData> Cases() => SearchCertificateFixtures.Fixtures()
        .Select(fixture => new TestCaseData(fixture.Name).SetName("CertificateOracle_" + fixture.Name));

    [TestCaseSource(nameof(Cases))]
    public void EveryLabeledShuffleAndEveryResultObeysFixedPolicyBounds(string name) {
        SearchCertificateFixtures.Fixture fixture = SearchCertificateFixtures.Fixtures().Single(value => value.Name == name);
        DrawActivationPriority? priority = fixture.Priority is null ? null : new(DrawActivationMode.CustomPriority, fixture.Priority);
        WorkBudget budget = new(CalculationWorkPolicy.Default);
        ExactCalculationResult upper = new DrawEffectCalculation(fixture.Deck, fixture.Hand, budget, priority)
            .Calculate(fixture.Combos, fixture.Groups);
        SearchAvailabilityCertificate certificate = SearchAvailabilityCertification.TryCalculate(
            fixture.Deck, fixture.Hand, fixture.Combos, fixture.Groups, upper, budget);
        // This oracle retains actual physical suffix identities. Its scheduling
        // dependency is the existing TEST-ONLY DrawSequenceOracle; its search,
        // selector and distinct-slot predicate share no production compiler/MDD.
        SearchCertificateOracle.Counts physical = SearchCertificateOracle.Enumerate(
            fixture.Deck, fixture.Hand, fixture.Combos, fixture.Groups, fixture.Priority,
            certificate.Total is null ? null : CheckTerminal);
        ExactProbability[] values = [upper.Total, .. upper.Combos, .. upper.Groups];
        for (int row = 0; row < values.Length; row++) {
            Assert.That(values[row].Numerator * physical.Outcomes,
                Is.EqualTo(physical.Wins[2][row] * values[row].Denominator), $"optimistic row {row}");
        }
        Assert.That(certificate.Status, Is.Not.EqualTo(SearchAvailabilityStatus.Error), certificate.Detail);
        Assert.That(certificate.Status, Is.Not.EqualTo(SearchAvailabilityStatus.Limited), certificate.Detail);
        if (certificate.Status == SearchAvailabilityStatus.NotCertified) {
            Assert.That(certificate.Total, Is.Null);
            return;
        }
        SearchAvailabilityInterval[] intervals = [certificate.Total!, .. certificate.Combos, .. certificate.Groups];
        for (int row = 0; row < intervals.Length; row++) {
            BigInteger lowerN = BigInteger.Parse(intervals[row].Lower.Numerator);
            BigInteger lowerD = BigInteger.Parse(intervals[row].Lower.Denominator);
            BigInteger upperN = BigInteger.Parse(intervals[row].Upper.Numerator);
            BigInteger upperD = BigInteger.Parse(intervals[row].Upper.Denominator);
            Assert.That(lowerN * physical.Outcomes, Is.LessThanOrEqualTo(physical.Wins[1][row] * lowerD), $"lower row {row}");
            Assert.That(upperN * physical.Outcomes, Is.GreaterThanOrEqualTo(physical.Wins[1][row] * upperD), $"upper row {row}");
            Assert.That(upperN * values[row].Denominator, Is.EqualTo(values[row].Numerator * upperD));
            if (certificate.Status == SearchAvailabilityStatus.Bounded) {
                Assert.That(lowerN * physical.Outcomes, Is.EqualTo(physical.Wins[0][row] * lowerD), $"elementary row {row}");
            }
            if (certificate.Status == SearchAvailabilityStatus.Exact) {
                Assert.That(intervals[row].IsExact, Is.True);
            }
        }

        void CheckTerminal(List<Card> hand, int remaining, int lost, bool[] expected) {
            WorkBudget terminalBudget = new(CalculationWorkPolicy.Default);
            List<Card> cards = fixture.Deck.Where(card => card.Active && card.Copies > 0).ToList();
            List<Card> retained = cards.Select(card => card.WithCopies(hand.Count(held => held.Id == card.Id)))
                .Where(card => card.Copies > 0).ToList();
            ExactCalculationResult lower = new SearchProbabilityCalculation([], 0, retained,
                new(cards, terminalBudget), remaining, terminalBudget, targetLoss: lost).Calculate(fixture.Combos, fixture.Groups, false);
            ExactProbability[] events = [lower.Total, .. lower.Combos, .. lower.Groups];
            for (int row = 0; row < events.Length; row++) {
                Assert.That(events[row].Numerator, Is.EqualTo(expected[row] ? events[row].Denominator : BigInteger.Zero), $"terminal row {row}");
            }
        }
    }

    [TestCase("plain", "550033", "658008", SearchAvailabilityStatus.Exact)]
    [TestCase("one-draw2", "4819127", "5593068", SearchAvailabilityStatus.Exact)]
    [TestCase("three-draw2", "850650419", "936887952", SearchAvailabilityStatus.Exact)]
    [TestCase("banish10", "4819127", "5593068", SearchAvailabilityStatus.Bounded)]
    [TestCase("mill2", "4819127", "5593068", SearchAvailabilityStatus.Bounded)]
    public async Task FortyCardAllRowsAndWirePreservePrimary(string name, string numerator, string denominator, SearchAvailabilityStatus status) {
        SessionState session = await Example(name);
        CalculationSnapshot off = CalculationSnapshot.Capture(session.Cards, session.Combos, session.HandSize, session.ComboGroups);
        CalculationSnapshot on = CalculationSnapshot.Capture(session.Cards, session.Combos, session.HandSize, session.ComboGroups,
            options: new(CertifySearchAvailability: true));
        ProbabilityCalculationResult plain = CalculationWire.ReadResult(CalculationWire.Execute(off.Json));
        ProbabilityCalculationResult certified = CalculationWire.ReadResult(CalculationWire.Execute(on.Json));
        AssertPrimary(plain, certified);
        SearchAvailabilityCertificate certificate = certified.SearchAvailability!;
        Assert.That(certificate.Status, Is.EqualTo(status), certificate.Detail);
        Assert.That(certificate.Total!.Upper, Is.EqualTo(new ProbabilityFraction(numerator, denominator)));
        Assert.That(certificate.Combos.Length, Is.EqualTo(8));
        Assert.That(certificate.Groups.Length, Is.EqualTo(2));
        if (name == "plain") {
            long[] numerators = [271496, 64470, 150480, 117819, 16446, 313972, 23346, 22091, 402929, 323132];
            SearchAvailabilityInterval[] rows = [.. certificate.Combos, .. certificate.Groups];
            for (int index = 0; index < rows.Length; index++) {
                Assert.That(BigInteger.Parse(rows[index].Upper.Numerator) * 658008,
                    Is.EqualTo(numerators[index] * BigInteger.Parse(rows[index].Upper.Denominator)));
            }
        }
        TestContext.Out.WriteLine(JsonSerializer.Serialize(certificate));
    }

    [Test]
    public async Task OptionalWorkExhaustionPreservesCompletePrimaryAndOldWireDefaults() {
        SessionState session = await Example("mill2");
        WorkBudget budget = new(CalculationWorkPolicy.Default);
        ExactCalculationResult exact = new DrawEffectCalculation(session.Cards, session.HandSize, budget).Calculate(session.Combos, session.ComboGroups);
        ProbabilityCalculationResult expected = exact.ToPublic(session.Combos, session.ComboGroups, budget);
        CalculationSnapshot snapshot = CalculationSnapshot.Capture(session.Cards, session.Combos, session.HandSize, session.ComboGroups,
            new(budget.Spent), options: new(CertifySearchAvailability: true));
        ProbabilityCalculationResult actual = CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json));
        AssertPrimary(expected, actual);
        Assert.That(actual.SearchAvailability!.Status, Is.EqualTo(SearchAvailabilityStatus.Limited));
        Assert.That(actual.SearchAvailability.Total, Is.Null);
        Assert.That(actual.SearchAvailability.Detail, Does.Contain("Work"));
        CalculationSnapshot legacy = CalculationSnapshot.Capture(session.Cards, session.Combos, session.HandSize, session.ComboGroups);
        Assert.That(legacy.Json, Does.Not.Contain("CertifySearchAvailability"));
        Assert.That(CalculationWire.ReadResult(CalculationWire.Execute(legacy.Json)).SearchAvailability, Is.Null);
    }

    [Test]
    public void OptionalStorageExhaustionDoesNotPublishPartialBounds() {
        // Many empty groups are cheap for the primary counter, but retaining an
        // exact interval for every output row exceeds unchanged Standard storage.
        List<Card> cards = [new([], 1, id: "t"), new([], 1, id: "f")];
        List<Combo> combos = [new([], cards: [new("t", 1, 1)])];
        List<ComboGroup> groups = Enumerable.Range(0, 17000).Select(index => new ComboGroup($"g{index}", "Group")).ToList();
        ProbabilityCalculatorService engine = new();
        ProbabilityCalculationResult primary = engine.CalculateProbabilityResults(cards, combos, 1, groups);
        ProbabilityCalculationResult result = engine.CalculateProbabilityResults(cards, combos, 1, groups,
            CalculationWorkPolicy.Default, new(CertifySearchAvailability: true));
        AssertPrimary(primary, result);
        SearchAvailabilityCertificate certificate = result.SearchAvailability!;
        Assert.That(certificate.Status, Is.EqualTo(SearchAvailabilityStatus.Limited));
        Assert.That(certificate.Detail, Does.Contain("Storage"));
        Assert.That(certificate.Total, Is.Null);
        Assert.That(certificate.Groups, Is.Empty);
    }

    [Test]
    public async Task OpeningOnlySnapshotOwnsItsEffectsAndCertificate() {
        SessionState session = await Example("mill2");
        CalculationSnapshot snapshot = CalculationSnapshot.Capture(session.Cards, session.Combos, session.HandSize, session.ComboGroups,
            drawEffectsEnabled: false, options: new(CertifySearchAvailability: true));
        session.Cards.Clear();
        session.Combos.Clear();
        ProbabilityCalculationResult result = CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json));
        Assert.That(result.SearchAvailability!.Status, Is.EqualTo(SearchAvailabilityStatus.NotApplicable));
        Assert.That(result.SearchAvailability.Combos.Length, Is.EqualTo(8));
        Assert.That(result.SearchAvailability.Total!.IsExact, Is.True);
    }

    [Test]
    public void LostTargetIsAbsentFromPhysicalDeckAndFailedBranchesKeepTheirMass() {
        foreach (string name in new[] { "minimal-hidden-only-target", "minimal-milled-only-target", "minimal-filtered-target-discard" }) {
            SearchCertificateFixtures.Fixture fixture = SearchCertificateFixtures.Fixtures().Single(value => value.Name == name);
            SearchCertificateOracle.Counts counts = SearchCertificateOracle.Enumerate(fixture.Deck, fixture.Hand, fixture.Combos, fixture.Groups);
            bool filtered = name == "minimal-filtered-target-discard";
            Assert.That(counts.Outcomes, Is.EqualTo(new BigInteger(120)));
            Assert.That(counts.Wins[0][0] * (filtered ? 2 : 10), Is.EqualTo(counts.Outcomes * (filtered ? 1 : 7)));
            Assert.That(counts.Wins[1][0] * (filtered ? 2 : 30), Is.EqualTo(counts.Outcomes * (filtered ? 1 : 23)));
            Assert.That(counts.Wins[2][0] * (filtered ? 30 : 6), Is.EqualTo(counts.Outcomes * (filtered ? 17 : 5)));
            Assert.That(counts.Wins[1][0], Is.LessThan(counts.Wins[2][0]));
            if (filtered) {
                Assert.That(counts.Failed, Is.GreaterThan(BigInteger.Zero));
            }
        }
    }

    [TestCase(0)]
    [TestCase(2)]
    public void EmptyResultsAndExhaustedDeckHaveExactAvailability(int handSize) {
        List<Card> cards = [new([], 1, id: "s", search: new(CardSelector.Any, false)), new([], 1, id: "f")];
        ProbabilityCalculationResult result = new ProbabilityCalculatorService().CalculateProbabilityResults(
            cards, [], handSize, [], CalculationWorkPolicy.Default, new(CertifySearchAvailability: true));
        Assert.That(result.TotalProbability, Is.Zero);
        Assert.That(result.SearchAvailability!.Status, Is.EqualTo(SearchAvailabilityStatus.Exact));
        Assert.That(result.SearchAvailability.Total!.Lower, Is.EqualTo(new ProbabilityFraction("0", "1")));
        Assert.That(result.SearchAvailability.Combos, Is.Empty);
    }

    [Test]
    public void ChangedDiscardPolicyCanBeatTheFixedPolicyUpperAndIsNotCertified() {
        CategoryBase a = new("A");
        Card draw = new([], 1, id: "draw", drawCount: 1, postDrawDiscardCount: 1);
        Card target = new([a], 1, id: "a");
        Card filler = new([], 1, id: "f");
        Card search = new([], 1, id: "s", search: new(CardSelector.For(a), false));
        List<Combo> combos = [new([new(a, 1, 3)]), new([new(a, 0, 0)])];
        List<Card>? fixedHand = DrawSequenceOracle.Resolve([draw, target, filler, search], 2, combos);
        Assert.That(SearchCertificateHandOracle.MatchesHand(fixedHand!, combos[1]), Is.False);
        Assert.That(SearchCertificateHandOracle.MatchesHand([filler], combos[1]), Is.True,
            "Choosing to discard A changes the policy and invalidates a universal gameplay upper claim.");
        ProbabilityCalculationResult result = new ProbabilityCalculatorService().CalculateProbabilityResults(
            [draw, target, filler, search], combos, 2, null, CalculationWorkPolicy.Default, new(CertifySearchAvailability: true));
        Assert.That(result.SearchAvailability!.Status, Is.EqualTo(SearchAvailabilityStatus.NotCertified));
    }

    internal static void AssertPrimary(ProbabilityCalculationResult expected, ProbabilityCalculationResult actual) {
        Assert.That(actual.TotalProbability, Is.EqualTo(expected.TotalProbability));
        Assert.That(actual.ComboProbabilities, Is.EqualTo(expected.ComboProbabilities));
        Assert.That(actual.GroupProbabilities, Is.EqualTo(expected.GroupProbabilities));
    }

    internal static async Task<SessionState> Example(string name) {
        SessionService codec = new(null!, new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());
        SessionState session = await codec.LoadSessionAsync(File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory,
            "Fixtures", "search_example_session_state.json")));
        if (name == "plain") {
            return session;
        }
        int index = session.Cards.FindIndex(card => card.Name == "Mulcharmy Fuwalos");
        Card original = session.Cards[index];
        int copies = name == "three-draw2" ? 3 : 1;
        Card effect = original.WithCopies(copies).WithDrawCount(2).WithDrawOncePerTurn(name != "three-draw2");
        if (name == "banish10") {
            effect = effect.WithPreDrawDeckRemovalCount(10);
        }
        if (name == "mill2") {
            effect = effect.WithPostDrawDeckMillCount(2);
        }
        session.Cards[index] = effect;
        if (copies < original.Copies) {
            session.Cards.Add(new(original.Categories, original.Copies - copies, "Non-draw Fuwalos", id: "certificate-inert-fuwalos", externalCardId: original.ExternalCardId));
        }
        return session;
    }
}
