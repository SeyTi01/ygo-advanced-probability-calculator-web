using System.Security.Cryptography;
using System.Numerics;
using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.Session;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Certification;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Counting;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.DrawResolution;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Resources;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator.Search;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture, NonParallelizable]
public sealed class SelectiveSurvivorTest {
    [Test]
    public async Task SuppliedSessionCompletesExactWithUnchangedInteractiveLimits() {
        SessionState state = await Source();
        Assert.That(state.Cards.Count, Is.EqualTo(22));
        Assert.That(state.Cards.Sum(card => card.Copies), Is.EqualTo(40));
        Card cost = state.Cards.Single(card => card.PreDrawRemovalCount > 0);
        Card payment = state.Cards.Single(card => cost.PreDrawSelector.Matches(card));
        Assert.That(payment.Copies, Is.EqualTo(1));
        Assert.That(cost.MaximumActivations, Is.EqualTo(int.MaxValue));
        Assert.That(state.Cards.Where(card => card.Search is not null).Any(card => card.Search!.Matches(payment)), Is.False);
        Assert.That(state.Combos.SelectMany(combo => combo.AllCategories).Any(requirement => requirement.Selector.Matches(payment)), Is.True);
        WorkBudget budget = new(CalculationWorkPolicy.Interactive);
        DrawEffectCalculation execution = new(state.Cards, state.HandSize, budget, state.DrawActivationPriority, physicalSearch: true);
        ExactCalculationResult rows = execution.Calculate(state.Combos, state.ComboGroups);
        ProbabilityCalculationResult result = ProbabilityCalculatorService.CompleteResult(state.Combos, state.ComboGroups,
            CalculationModel.Exact, rows, budget, execution);
        Assert.That(result.Contract!.Accuracy, Is.EqualTo(CalculationAccuracy.Exact));
        Assert.That(result.Contract.SolverRoute, Is.EqualTo("SearchPhysicalDeferred"));
        Assert.That(result.SearchAvailability, Is.Null);
        Assert.That(budget.Spent, Is.LessThan(CalculationWorkPolicy.Interactive.WorkUnits));
        ProbabilityFraction[] expected = [
            new("404499773243054219", "436593827634307200"),
            new("1628544926949", "3441501743680"), new("45656038837", "356017421760"),
            new("71685087567", "237344947840"), new("2295964810807", "8849575912320"),
            new("29372831", "794681745"), new("1015178869130159", "1725667302902400"),
            new("1272043811", "28866277440"), new("331069292324833", "4026557040105600"),
            new("408639408589", "563154830784"), new("24815332707271", "40672293334400")
        ];
        ExactProbability[] actual = [rows.Total, .. rows.Combos, .. rows.Groups];
        Assert.That(actual.Select(value => value.ToFraction()), Is.EqualTo(expected));
    }

    [TestCase(0, false)] [TestCase(1, false)] [TestCase(2, false)] [TestCase(3, false)]
    [TestCase(1, true)] [TestCase(2, true)] [TestCase(3, true)]
    public void SelectivePaymentsAgreeWithChronologicalPhysicalOracle(int payments, bool splitAndPrioritize) {
        CategoryBase material = new("Material");
        CategoryBase target = new("Target");
        List<Card> cards = [
            new([target], 2, id: "cost-draw", drawCount: 1, drawOncePerTurn: false,
                preDrawRemovalCount: 1, preDrawSelector: CardSelector.For(material)),
            new([], 1, id: "hidden-draw", drawCount: 1, preDrawDeckRemovalCount: 1),
            new([], 1, id: "search", search: new(CardSelector.For(target), false)),
            new([target], 1, id: "ordinary"),
            new([], 3 - payments, id: "filler"),
            new([material, target], payments, id: "payment")
        ];
        if (splitAndPrioritize && payments > 1) {
            cards[^1] = cards[^1].WithCopies(payments - 1);
            cards.Add(new([material], 1, id: "second-payment"));
        }
        cards = cards.Where(card => card.Copies > 0).ToList();
        List<ComboGroup> groups = [new("empty", "Empty"), new("g", "Union"), new("h", "Exclusion")];
        List<Combo> combos = [
            new([new(target, 1, 3), new(material, 1, 3)], groupId: "g"),
            new([ComboCategory.SameCard([material, target], 1, 1)], groupId: "g"),
            new([new(target, 0, 0)], groupId: "h"),
            new([], groupId: "h", cards: [new("cost-draw", 0, 0), new("search", 0, 0)]),
            new([], groupId: "g", alternativeGroups: [new([
                ComboAlternative.For(new ComboCard("payment", 1, 2)), ComboAlternative.For(new ComboCategory(target, 2, 3))])])
        ];
        string[]? order = splitAndPrioritize ? ["hidden-draw", "cost-draw"] : null;
        DrawActivationPriority? priority = order is null ? null : new(DrawActivationMode.CustomPriority, order);
        SearchCertificateOracle.Counts oracle = SearchCertificateOracle.Enumerate(cards, 2, combos, groups, order);
        WorkBudget budget = new(CalculationWorkPolicy.Interactive);
        BigInteger mass = 0;
        PostDrawCalculation compact = new(cards, 2, budget, priority) { ObserveTerminal = (_, weight) => mass += weight };
        ExactCalculationResult actual = compact.CalculateSearch(combos, groups, false, new(cards, budget), physical: true);
        Assert.That(compact.Route, Is.EqualTo(SolverRoute.SearchPhysicalDeferred));
        Assert.That(mass + oracle.Failed, Is.EqualTo(oracle.Outcomes));
        ExactProbability[] actualRows = [actual.Total, .. actual.Combos, .. actual.Groups];
        for (int row = 0; row < actualRows.Length; row++) {
            Assert.That(actualRows[row].Numerator * oracle.Outcomes,
                Is.EqualTo(oracle.Wins[1][row] * actualRows[row].Denominator), $"oracle row {row}");
        }
        WorkBudget referenceBudget = new(CalculationWorkPolicy.Interactive);
        PostDrawCalculation reference = new(cards, 2, referenceBudget, priority);
        ExactCalculationResult old = reference.CalculateSearch(combos, groups, false, new(cards, referenceBudget),
            physical: true, method: PhysicalEnumerationMethod.Streaming);
        ExactProbability[] oldRows = [old.Total, .. old.Combos, .. old.Groups];
        Assert.That(actualRows.Select(value => value.ToFraction()), Is.EqualTo(oldRows.Select(value => value.ToFraction())));
    }

    [Test, Explicit("Phase attribution for the frozen session")]
    public async Task Diagnose() {
        SessionState state = await Source();
        SearchCountingDiagnostics diagnostics = new();
        WorkBudget budget = new(CalculationWorkPolicy.Interactive) { SearchDiagnostics = diagnostics };
        BigInteger mass = 0;
        PostDrawCalculation calculation = new(state.Cards, state.HandSize, budget, state.DrawActivationPriority) {
            ObserveTerminal = (_, weight) => mass += weight
        };
        try {
            ExactCalculationResult result = calculation.CalculateSearch(state.Combos, state.ComboGroups, false,
                new(state.Cards, budget), physical: true);
            BigInteger permutations = Enumerable.Range(1, 40).Aggregate(BigInteger.One, (product, count) => product * count);
            Assert.That(mass, Is.EqualTo(permutations));
            TestContext.Out.WriteLine(result.Total);
        }
        finally {
            TestContext.Out.WriteLine(JsonSerializer.Serialize(new { budget.Spent, budget.PeakEntries, budget.PeakCells,
                calculation.TerminalStates, calculation.DeferredRows, calculation.ReducedRows,
                diagnostics.Counts, diagnostics.Milliseconds }));
        }
    }

    [TestCase(2, false)] [TestCase(3, true)] [TestCase(6, true)]
    public void SharedCostsRemovableSearchersAndFailedPostDrawRequirements(int hand, bool postRemoval) {
        CategoryBase material = new("Material");
        CategoryBase target = new("Target");
        List<Card> cards = [
            new([target], 1, id: "first", drawCount: 1, preDrawRemovalCount: 1,
                preDrawSelector: CardSelector.For(material), postDrawDeckMillCount: 1),
            new([], 1, id: "second", drawCount: 1, preDrawRemovalCount: postRemoval ? 0 : 1,
                preDrawSelector: postRemoval ? null : CardSelector.For(material), postDrawDiscardCount: postRemoval ? 1 : 0,
                postDrawSelector: postRemoval ? CardSelector.For(material) : null),
            new([], 1, id: "hidden", drawCount: 1, preDrawDeckRemovalCount: 1, postDrawDeckMillCount: 1),
            new([material, target], 1, id: "payment"),
            new(postRemoval ? [material] : [], 1, id: "search", search: new(CardSelector.For(target), false)),
            new([], 1, id: "other-search", search: new(CardSelector.For(target), false)),
            new([target], 1, id: "target"), new([], 1, id: "filler")
        ];
        List<Combo> combos = [new([new(target, 1, 1)]), new([new(material, 0, 0)]),
            new([], cards: [new("first", 1, 8), new("search", 0, 0)])];
        string[] priority = ["second", "hidden", "first"];
        SearchCertificateOracle.Counts oracle = SearchCertificateOracle.Enumerate(cards, hand, combos, [], priority);
        WorkBudget budget = new(CalculationWorkPolicy.Interactive);
        BigInteger mass = 0;
        PostDrawCalculation execution = new(cards, hand, budget, new(DrawActivationMode.CustomPriority, priority)) {
            ObserveTerminal = (_, weight) => mass += weight
        };
        ExactCalculationResult result = execution.CalculateSearch(combos, [], false, new(cards, budget), physical: true);
        Assert.That(execution.Route, Is.EqualTo(SolverRoute.SearchPhysicalDeferred));
        Assert.That(mass + oracle.Failed, Is.EqualTo(oracle.Outcomes));
        ExactProbability[] rows = [result.Total, .. result.Combos];
        for (int row = 0; row < rows.Length; row++) {
            Assert.That(rows[row].Numerator * oracle.Outcomes, Is.EqualTo(oracle.Wins[1][row] * rows[row].Denominator));
        }
    }

    [TestCase(false)] [TestCase(true)]
    public void UnsupportedPoolObservationsRetainPhysicalFallback(bool activationRestriction) {
        CategoryBase target = new("Target");
        List<Card> cards = [new([target], 1, id: "target"), new([], 2, id: "filler"),
            new([], 1, id: "search", search: new(CardSelector.For(target), false)),
            new([], 1, id: "draw", drawCount: 1, preDrawRemovalCount: 1,
                preDrawSelector: activationRestriction ? CardSelector.For(target) : CardSelector.Any,
                firstActivationOnly: activationRestriction, postDrawDeckMillCount: 1)];
        List<Combo> combos = [new([new(target, 1, 2)])];
        WorkBudget budget = new(CalculationWorkPolicy.Default);
        PostDrawCalculation execution = new(cards, 2, budget);
        ExactCalculationResult result = execution.CalculateSearch(combos, [], false, new(cards, budget), physical: true);
        Assert.That(execution.Route, Is.EqualTo(SolverRoute.SearchPhysicalFrontier));
        SearchCertificateOracle.Counts oracle = SearchCertificateOracle.Enumerate(cards, 2, combos, []);
        Assert.That(result.Total.Numerator * oracle.Outcomes, Is.EqualTo(oracle.Wins[1][0] * result.Total.Denominator));
    }

    internal static async Task<SessionState> Source() {
        byte[] bytes = File.ReadAllBytes(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "desires_trade_in.json"));
        Assert.That(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            Is.EqualTo("6ea9f5e1689854aaf19e904909c638ea92eebf329fe140c177f8ac9fbaeedac5"));
        SessionService codec = new(null!, new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());
        return await codec.LoadSessionAsync(System.Text.Encoding.UTF8.GetString(bytes));
    }

    [Test, Explicit("Export frozen real-session benchmark inputs")]
    public async Task Export() {
        List<object> output = [];
        SessionService codec = new(null!, new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());
        foreach (string variant in new[] { "source", "trade-disabled", "desires-disabled", "both-disabled", "two-payments", "hidden-five", "no-search", "searchable-payment", "selective-post-removal" }) {
            SessionState state = await Source();
            if (variant == "searchable-payment") {
                Card cost = state.Cards.Single(card => card.PreDrawRemovalCount > 0);
                int source = state.Cards.FindIndex(card => card.Search is not null);
                Card search = state.Cards[source];
                state.Cards[source] = search.WithSearch(new(CardSelector.AnyOfSelectors([search.Search!.Selector, cost.PreDrawSelector]), false));
            }
            if (variant == "two-payments") {
                Card cost = state.Cards.Single(card => card.PreDrawRemovalCount > 0);
                int payment = state.Cards.FindIndex(card => cost.PreDrawSelector.Matches(card));
                int replacement = state.Cards.FindIndex(card => card.DrawCount is null && card.Search is null && card.Copies == 3);
                state.Cards[payment] = state.Cards[payment].WithCopies(2);
                state.Cards[replacement] = state.Cards[replacement].WithCopies(2);
            }
            for (int row = 0; row < state.Cards.Count; row++) {
                Card card = state.Cards[row];
                if ((variant is "trade-disabled" or "both-disabled" && card.PreDrawRemovalCount > 0)
                    || (variant is "desires-disabled" or "both-disabled" && card.PreDrawDeckRemovalCount > 0)) {
                    state.Cards[row] = card.WithEffect(new Card([]).EffectPlan, card.EffectProfile.AsCustom());
                }
                if (variant == "hidden-five" && card.PreDrawDeckRemovalCount > 0) {
                    state.Cards[row] = card.WithPreDrawDeckRemovalCount(5);
                }
                if (variant == "no-search" && card.Search is not null) {
                    state.Cards[row] = card.WithSearch(null);
                }
                if (variant == "selective-post-removal" && card.PreDrawRemovalCount > 0) {
                    state.Cards[row] = card.WithEffect(card.EffectPlan with {
                        HandCost = 0, HandCostSelector = CardSelector.Any,
                        RemovalCount = card.PreDrawRemovalCount, RemovalSelector = card.PreDrawSelector
                    }, card.EffectProfile.AsCustom());
                }
            }
            Assert.That(state.Cards.Sum(card => card.Copies), Is.EqualTo(40));
            foreach (CalculationModel mode in new[] { CalculationModel.Fast, CalculationModel.Exact }) {
                output.Add(new { Name = variant + "-" + mode,
                    Session = JsonSerializer.Deserialize<JsonElement>(codec.SerializeSession(state)),
                    Input = JsonSerializer.Deserialize<JsonElement>(
                    CalculationSnapshot.Capture(state.Cards, state.Combos, state.HandSize, state.ComboGroups,
                        CalculationWorkPolicy.Interactive, options: new(state.DrawActivationPriority, Mode: mode)).Json) });
            }
        }
        File.WriteAllText(Environment.GetEnvironmentVariable("YGO_SELECTIVE_FIXTURES")
            ?? throw new InvalidOperationException("Set YGO_SELECTIVE_FIXTURES."), JsonSerializer.Serialize(output));
    }
}
