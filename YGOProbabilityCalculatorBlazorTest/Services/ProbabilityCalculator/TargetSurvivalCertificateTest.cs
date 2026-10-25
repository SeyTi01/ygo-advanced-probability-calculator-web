using System.Text.Json;
using System.Text.Json.Nodes;
using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.Session;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Counting;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Certification;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Search;
using DrawResolutionResult = YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.DrawResolution.DrawResolution;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture, NonParallelizable]
public sealed class TargetSurvivalCertificateTest {
    internal static SessionState Motivating(int required = 5) {
        CategoryBase monster = new("Monster", CategorySource.Metadata, "kind:monster");
        return new() {
            Cards = [
                new([], 3, "Pot of Desires", id: "desires", externalCardId: 35261759,
                    drawCount: 2, preDrawDeckRemovalCount: 10,
                    effectProfile: new(EffectSource.BuiltIn, 35261759, 1,
                        "Banish 10 unseen deck cards, then draw 2; once per turn. Requires 12 cards left.", EffectSupport.ModeledWithinRules)),
                new([monster], 5, "Combo", id: "target", manualMetadataCategoryKeys: ["kind:monster"]),
                new([], 32, "Search", id: "search", search: new(CardSelector.For(monster), false), oncePerTurn: false,
                    effectProfile: new(EffectSource.Custom))
            ],
            Combos = [new([], "Combo", cards: [new("target", required, 5, RequirementMaximumMode.HandSize)])],
            DrawActivationPriority = new(DrawActivationMode.CustomPriority, ["desires", "search"]),
            HandSize = 5
        };
    }

    [Test]
    public async Task FortyCardProofMatchesIndependentOpeningBanishAndDrawPartitions() {
        SessionState state = Motivating();
        // Independent chronological partitions of labeled copies: opening hand,
        // then ten hidden cards, then two drawn cards. No production count-state,
        // binomial helper, search compiler or physical solver supplies expectations.
        BigInteger denominator = Binomial(40, 5) * Binomial(35, 10) * Binomial(25, 2);
        BigInteger survive = Binomial(37, 5) * Binomial(35, 10) * Binomial(25, 2);
        BigInteger success = survive;
        for (int sources = 1; sources <= 3; sources++) {
            for (int targets = 0; targets <= 5 - sources; targets++) {
                BigInteger opening = Binomial(3, sources) * Binomial(5, targets) * Binomial(32, 5 - sources - targets);
                for (int hiddenSources = 0; hiddenSources <= 3 - sources; hiddenSources++) {
                    BigInteger hidden = Binomial(3 - sources, hiddenSources)
                        * Binomial(32 - (5 - sources - targets), 10 - hiddenSources);
                    survive += opening * hidden * Binomial(25, 2);
                    for (int drawnSources = 0; drawnSources <= 2; drawnSources++) {
                        // All targets survive. Five useful slots are still required:
                        // retained + drawn Desires cannot substitute for a target.
                        if (sources + drawnSources <= 2) {
                            success += opening * hidden * Binomial(3 - sources - hiddenSources, drawnSources)
                                * Binomial(25 - (3 - sources - hiddenSources), 2 - drawnSources);
                        }
                    }
                }
            }
        }
        ExactProbability eventProbability = new(survive, denominator);
        ExactProbability actualProbability = new(success, denominator);
        ProbabilityCalculatorService engine = new();
        foreach (CalculationWorkPolicy work in new[] { CalculationWorkPolicy.Default, CalculationWorkPolicy.Interactive }) {
            ProbabilityCalculationResult fast = engine.CalculateProbabilityResults(state.Cards, state.Combos, 5, [], work, new(state.DrawActivationPriority));
            ProbabilityCalculationResult exact = engine.CalculateProbabilityResults(state.Cards, state.Combos, 5, [], work, new(state.DrawActivationPriority, Mode: CalculationModel.Exact));
            Assert.That(fast.Contract!.Fractions!.Total, Is.EqualTo(new ProbabilityFraction("1969", "1976")));
            Assert.That(exact.Contract!.Fractions!.Total, Is.EqualTo(actualProbability.ToFraction()));
            Assert.That(exact.Contract.Fractions.Total, Is.EqualTo(new ProbabilityFraction("35017811", "47851804")));
            Assert.That(exact.SearchAvailability, Is.Null, "Exact must not run the optional certificate");
            foreach (SearchAvailabilityInterval row in new[] { fast.SearchAvailability!.Total!, fast.SearchAvailability.Combos[0] }) {
                Assert.That(row.Lower, Is.EqualTo(new ProbabilityFraction("17290681", "26100984")));
                Assert.That(row.Upper, Is.EqualTo(eventProbability.ToFraction()));
                Assert.That(survive * 10000, Is.LessThanOrEqualTo(denominator * 7325));
                Assert.That(success, Is.LessThan(survive), "Survival alone is not sufficient");
            }
        }
        SessionService codec = new(null!, new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());
        SessionState loaded = await codec.LoadSessionAsync(codec.SerializeSession(state));
        Assert.That(loaded.Cards.Single(card => card.Id == "search").Copies, Is.EqualTo(32));
        Assert.That(loaded.Cards.Single(card => card.Id == "target").ManualMetadataCategoryKeys, Does.Contain("kind:monster"));
        CalculationSnapshot snapshot = CalculationSnapshot.Capture(loaded.Cards, loaded.Combos, loaded.HandSize, loaded.ComboGroups, options: new(loaded.DrawActivationPriority));
        ProbabilityCalculationResult wire = CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json));
        Assert.That(wire.SearchAvailability!.Total!.Upper, Is.EqualTo(eventProbability.ToFraction()));
    }

    [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)]
    [TestCase(4)] [TestCase(5)] [TestCase(6)] [TestCase(7)]
    [TestCase(8)] [TestCase(9)] [TestCase(10)] [TestCase(11)]
    public void LabeledCopiesProveNestedIntervalsForEveryRow(int variant) {
        CategoryBase a = new("A");
        List<Card> deck = [new([a], 2, "same", id: "t"), new([a], 1, "same", id: "b"),
            new([], 2, "source", id: "s", search: new(CardSelector.For(a), false), oncePerTurn: variant % 3 == 0),
            new([], 1, "filler", id: "f"),
            new([], 2, "same", id: "d", drawCount: 2, drawOncePerTurn: variant % 2 == 0,
                preDrawDeckRemovalCount: variant % 3 != 1 ? 1 : 0,
                postDrawDeckMillCount: variant % 3 != 0 ? 1 : 0)];
        if (variant >= 8) {
            deck[4] = new([], 1, "same", id: "d", drawCount: 1, preDrawDeckRemovalCount: 1);
            deck.Add(new([], 1, "same", id: "e", drawCount: 1, postDrawDeckMillCount: 1));
        }
        List<Combo> combos = [new([], "All t", groupId: "all", cards: [new("t", 2, 2)]),
            new([], "Both identities", groupId: "all", cards: [new("t", 2, 5), new("b", 1, 5)]),
            new([new(a, 1, 5)], "Distinct extra role", groupId: "all", cards: [new("t", 2, 5)]),
            new([], "Redundant", groupId: "mixed", cards: [new("t", 1, 5)]),
            new([], "Zero", groupId: "mixed", cards: [new("t", 0, 0)]),
            new([], "Alternative only", groupId: "mixed", alternativeGroups: [new([
                ComboAlternative.For(new ComboCard("t", 2, 5)), ComboAlternative.For(new ComboCard("f", 1, 5))])]),
            new([new(a, 0, 2)], "Maximum and unconditional", cards: [new("t", 2, 5)]),
            new([], "Unconditional plus alternative", cards: [new("t", 2, 5)], alternativeGroups: [new([
                ComboAlternative.For(new ComboCard("b", 1, 5)), ComboAlternative.For(new ComboCard("f", 1, 5))])]),
            new([], "Repeated identity bounds", cards: [new("t", 2, 5), new("t", 2, 5)]),
            new([], "Consumed source", cards: [new("d", deck.Single(card => card.Id == "d").Copies, 5)])];
        List<ComboGroup> groups = [new("all", "All"), new("mixed", "Mixed"), new("empty", "Empty")];
        if (variant % 2 != 0) { deck.Reverse(); }
        DrawActivationPriority priority = SearchProjectionTestPolicy.DrawFirst(deck);
        WorkBudget budget = new(CalculationWorkPolicy.Interactive);
        DrawEffectCalculation execution = new(deck, 2, budget, priority);
        ExactCalculationResult primary = execution.Calculate(combos, groups);
        ExactCalculationResult baseline = execution.SearchExecution!.CalculateLower(combos, groups, false);
        if (execution.SearchExecution.CanRefineKnownSourceLoss()) {
            baseline = execution.SearchExecution.CalculateLower(combos, groups, false, baseline);
        }
        SearchAvailabilityCertificate certificate = SearchAvailabilityCertification.TryCalculate(combos, groups, primary, budget, execution);
        // Repeated bounds on the identical direct selector intersect in the
        // existing model; they do not create another positive role. The generic
        // labeled oracle expects normalized roles. Keep the duplicate in the
        // production input to exercise identity deduplication in the new proof.
        List<Combo> oracleCombos = combos.Select(combo => combo.WithCards(combo.Cards
            .GroupBy(card => card.CardId, StringComparer.Ordinal)
            .Select(rows => new ComboCard(rows.Key, rows.Max(card => card.MinCount), rows.Min(card => card.MaxCount))))).ToList();
        SearchCertificateOracle.Counts physical = SearchCertificateOracle.Enumerate(deck, 2, oracleCombos, groups, priority.CardIds.ToArray());
        Assert.That(certificate.Status, Is.AnyOf(SearchAvailabilityStatus.Bounded, SearchAvailabilityStatus.Exact), certificate.Detail);
        Assert.That(certificate.Detail, Does.Not.Contain("refinement failed"));
        SearchAvailabilityInterval[] intervals = [certificate.Total!, .. certificate.Combos, .. certificate.Groups];
        ExactProbability[] oldLower = [baseline.Total, .. baseline.Combos, .. baseline.Groups];
        ExactProbability[] oldUpper = [primary.Total, .. primary.Combos, .. primary.Groups];
        for (int row = 0; row < intervals.Length; row++) {
            ExactProbability lower = Parse(intervals[row].Lower);
            ExactProbability upper = Parse(intervals[row].Upper);
            using (Assert.EnterMultipleScope()) {
                LessOrEqual(oldLower[row], lower, $"old lower row {row}");
                LessOrEqual(lower, new(physical.Wins[1][row], physical.Outcomes), $"lower row {row}");
                LessOrEqual(new(physical.Wins[1][row], physical.Outcomes), upper, $"upper row {row}");
                LessOrEqual(upper, oldUpper[row], $"old upper row {row}");
            }
        }
    }

    [TestCase(0)] [TestCase(4096)] [TestCase(4500)] [TestCase(5000)] [TestCase(6000)] [TestCase(10000)]
    public void OptionalBudgetLimitRetainsCompletedPrimaryAndLower(int extra) {
        SessionState state = Motivating();
        WorkBudget routing = new(CalculationWorkPolicy.Default);
        Assert.That(UnifiedActivationRouting.Required(state.Cards, 5, state.DrawActivationPriority, routing), Is.False);
        ProbabilityCalculationResult result = new ProbabilityCalculatorService().CalculateProbabilityResults(
            state.Cards, state.Combos, 5, [], new(7561 + routing.Spent + extra), new(state.DrawActivationPriority));
        Assert.That(result.Contract!.Fractions!.Total, Is.EqualTo(new ProbabilityFraction("1969", "1976")));
        Assert.That(result.SearchAvailability!.Total!.Lower, Is.EqualTo(new ProbabilityFraction("17290681", "26100984")));
        Assert.That(result.SearchAvailability.Total.Upper, Is.AnyOf(new ProbabilityFraction("1969", "1976"), new ProbabilityFraction("1030859", "1407406")));
        Assert.That(result.SearchAvailability.Status, Is.EqualTo(SearchAvailabilityStatus.Bounded));
        if (extra == 4500) { Assert.That(result.SearchAvailability.Detail, Does.Contain("Limited survival refinement")); }
    }

    [TestCase(64)] [TestCase(65)] [TestCase(255)] [TestCase(256)] [TestCase(257)]
    public void CopyCapDoesNotTruncateCountsOrChangePrimary(int copies) {
        SessionState state = Motivating();
        state.Cards[2] = state.Cards[2].WithCopies(copies - 8);
        WorkBudget budget = new(CalculationWorkPolicy.Default);
        DrawEffectCalculation execution = new(state.Cards, 5, budget, state.DrawActivationPriority);
        ExactCalculationResult primary = execution.Calculate(state.Combos, []);
        SearchAvailabilityCertificate certificate = SearchAvailabilityCertification.TryCalculate(state.Combos, [], primary, budget, execution);
        Assert.That(certificate.Total, Is.Not.Null);
        if (copies > 64) { Assert.That(certificate.Total!.Upper, Is.EqualTo(primary.Total.ToFraction())); }
        Assert.That(budget.PeakEntries, Is.LessThanOrEqualTo(CalculationStoragePolicy.Standard.EntryLimit));
        Assert.That(budget.PeakCells, Is.LessThanOrEqualTo(CalculationStoragePolicy.Standard.CellLimit));
    }

    private static ExactProbability Parse(ProbabilityFraction value) => new(BigInteger.Parse(value.Numerator), BigInteger.Parse(value.Denominator));

    private static void LessOrEqual(ExactProbability left, ExactProbability right, string message) =>
        Assert.That(left.Numerator * right.Denominator, Is.LessThanOrEqualTo(right.Numerator * left.Denominator), message);

    private static BigInteger Binomial(int n, int k) {
        if (k < 0 || k > n) { return 0; }
        BigInteger numerator = 1;
        BigInteger denominator = 1;
        for (int i = 0; i < k; i++) {
            numerator *= n - i;
            denominator *= i + 1;
        }
        return numerator / denominator;
    }

    [TestCase("Lower", "1", "1")]
    [TestCase("Upper", "1", "1")]
    [TestCase("Upper", "1", "2")]
    [TestCase("Upper", "1", "0")]
    public void WorkerStillRejectsInvalidAndReversedIntervals(string field, string numerator, string denominator) {
        SessionState state = Motivating();
        JsonObject response = JsonNode.Parse(CalculationWire.Execute(CalculationSnapshot.Capture(
            state.Cards, state.Combos, 5, [], options: new(state.DrawActivationPriority)).Json))!.AsObject();
        response["Result"]!["SearchAvailability"]!["Total"]![field] = new JsonObject {
            ["Numerator"] = numerator, ["Denominator"] = denominator
        };
        Assert.Throws<InvalidOperationException>(() => CalculationWire.ReadResult(response.ToJsonString()));
    }

    [Test]
    public void OversizedScenarioAndIdentityProofsSkipWithoutSpendingTheirRequest() {
        SessionState state = Motivating();
        ExactCalculationResult primary = new(ExactProbability.One, [ExactProbability.One], []);
        DrawResolutionResult oversized = new(Enumerable.Range(0, 65).ToDictionary(
            count => new DrawComposition(count, [0, 0]), _ => ExactProbability.One));
        WorkBudget budget = new(CalculationWorkPolicy.Default);
        Assert.That(TargetSurvivalUpperBound.TryCalculate(state.Cards, [state.Cards[0]], oversized, state.Combos, [], primary, budget), Is.Null);
        Assert.That(budget.Spent, Is.Zero);
        Assert.That(budget.PeakCells, Is.Zero);
        state.Combos[0] = new([], cards: Enumerable.Repeat(new ComboCard("target", 5, 5), 5));
        DrawResolutionResult small = new(new() { [new(5, [0, 35])] = ExactProbability.One });
        Assert.That(TargetSurvivalUpperBound.TryCalculate(state.Cards, [state.Cards[0]], small, state.Combos, [], primary, budget), Is.Null);
        Assert.That(budget.Spent, Is.LessThan(100));
    }

    [Test, Explicit("Frozen survival native/WASM matrix export")]
    public async Task Export() {
        List<(string Name, SessionState State)> cases = [("mandatory-five", Motivating()), ("redundant-one", Motivating(1))];
        foreach (string name in new[] { "plain", "one-draw2", "banish10", "mill2" }) {
            cases.Add(("vs-k9-" + name, await SearchAvailabilityCertificateTest.Example(name)));
        }
        foreach (string name in new[] { "known-source-two-targets", "known-source-opt-False-searchable-True-hidden-1",
            "deferred-loss-cost-True-first-False", "ordered-pooling-3-reversed-False" }) {
            SearchCertificateFixtures.Fixture fixture = SearchCertificateFixtures.Fixtures().Single(value => value.Name == name);
            cases.Add((name, new() { Cards = fixture.Deck, HandSize = fixture.Hand, Combos = fixture.Combos,
                ComboGroups = fixture.Groups, DrawActivationPriority = SearchProjectionTestPolicy.DrawFirst(fixture.Deck, fixture.Priority) }));
        }
        SessionState ordinary = Motivating();
        for (int row = 0; row < ordinary.Cards.Count; row++) {
            ordinary.Cards[row] = new(ordinary.Cards[row].Categories, ordinary.Cards[row].Copies, id: ordinary.Cards[row].Id);
        }
        cases.Add(("no-effects", ordinary));
        List<object> output = [];
        foreach ((string name, SessionState state) in cases) {
            foreach (CalculationWorkPolicy policy in new[] { CalculationWorkPolicy.Default, CalculationWorkPolicy.Interactive }) {
                foreach (CalculationModel mode in new[] { CalculationModel.Fast, CalculationModel.Exact }) {
                    output.Add(new { Name = name + "-" + policy.WorkUnits + "-" + mode,
                        Input = JsonSerializer.Deserialize<JsonElement>(CalculationSnapshot.Capture(state.Cards, state.Combos,
                            state.HandSize, state.ComboGroups, policy, options: new(state.DrawActivationPriority, Mode: mode)).Json) });
                }
            }
        }
        SessionState opening = Motivating();
        output.Add(new { Name = "opening", Input = JsonSerializer.Deserialize<JsonElement>(CalculationSnapshot.Capture(opening.Cards,
            opening.Combos, opening.HandSize, opening.ComboGroups, options: new(Mode: CalculationModel.OpeningHand)).Json) });
        File.WriteAllText(Environment.GetEnvironmentVariable("YGO_SURVIVAL_FIXTURES")
            ?? throw new InvalidOperationException("Set YGO_SURVIVAL_FIXTURES."), JsonSerializer.Serialize(output));
    }
}
