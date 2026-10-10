using System.Diagnostics;
using System.Text.Json;
using Microsoft.JSInterop;
using Moq;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.Session;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

// Export the exact worker inputs and reference results for a published-browser run.
// Export: YGO_DRAW_BENCHMARK_OUTPUT=<directory> dotnet test -c Release
//   --filter FullyQualifiedName~DrawEffectsBenchmark.ExportWorkerCases
// YGO_DRAW_BENCHMARK_WORK overrides only this opt-in measurement's allowance.
// Timing is observational, never a regression assertion; use the worker harness
// in scripts/background-calculation/measure-draw-effects.cjs for browser timing.
[TestFixture, Explicit("Draw-effect worker benchmark fixtures")]
public class DrawEffectsBenchmark {
    [Test]
    public async Task ProfileNoRetainedScenarios() {
        SessionService codec = new(Mock.Of<IJSRuntime>(), new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());
        string json = File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "example_session_state.json"));
        SessionState session = await codec.LoadSessionAsync(json);
        List<Card> ordinary = session.Cards.Where(card => card.Name != "Mulcharmy Fuwalos").ToList();
        foreach (int hand in new[] { 5, 7, 9, 11 }) {
            WorkBudget budget = new(new(500_000_000));
            Stopwatch timer = Stopwatch.StartNew();
            ComboEventCompiler compiler = new(ordinary, hand, budget);
            List<List<CompiledEvent?>> compiled = session.Combos.Select(compiler.CompileCombo).ToList();
            long compilation = budget.Spent;
            double compilationMs = timer.Elapsed.TotalMilliseconds;
            // Deliberately measure the old empty projection separately: it loses
            // role metadata even though these constraints have no fixed copies.
            ScenarioConditioner conditioner = new(ordinary.Count, hand, [], budget);
            List<List<CompiledEvent?>> projected = compiled.Select(events => events.Select(conditioner.Project).ToList()).ToList();
            long projection = budget.Spent - compilation;
            double projectionMs = timer.Elapsed.TotalMilliseconds - compilationMs;
            ExactProbability? expected = null;
            foreach ((string label, List<List<CompiledEvent?>> events) in new[] { ("projected", projected), ("roles", compiled) }) {
                ExactHandCounter counter = new(ordinary, hand, budget);
                EventUnionEvaluator evaluator = new(ordinary.Count, hand, budget, compiler, counter);
                long before = budget.Spent;
                double start = timer.Elapsed.TotalMilliseconds;
                ExactProbability total = evaluator.UnionExact(events.SelectMany(route => route).ToList());
                if (expected is { } previous) {
                    Assert.That(total.Numerator * previous.Denominator, Is.EqualTo(previous.Numerator * total.Denominator));
                }
                expected = total;
                long unionWork = budget.Spent - before;
                double unionMs = timer.Elapsed.TotalMilliseconds - start;
                before = budget.Spent;
                start = timer.Elapsed.TotalMilliseconds;
                foreach (List<CompiledEvent?> route in events) {
                    evaluator.UnionExact(route);
                }
                foreach (ComboGroup group in session.ComboGroups) {
                    evaluator.UnionExact(events.Where((_, index) => session.Combos[index].GroupId == group.Id).SelectMany(route => route).ToList());
                }
                TestContext.Out.WriteLine($"hand={hand}; {label}; compile={compilation}/{compilationMs:F2}ms; project={projection}/{projectionMs:F2}ms; totalUnion={unionWork}/{unionMs:F2}ms; rows={budget.Spent - before}/{timer.Elapsed.TotalMilliseconds - start:F2}ms; p={total.ToDouble(budget):R}");
            }
        }
    }

    [Test]
    public async Task ExportWorkerCases() {
        CategoryBase starter = new("Starter");
        CategoryBase extender = new("Extender");
        List<Combo> combos = [new([new(starter, 1, 0, RequirementMaximumMode.HandSize),
            new(extender, 1, 0, RequirementMaximumMode.HandSize)], "Route", groupId: "g")];
        List<ComboGroup> groups = [new("g", "Group")];
        List<(string Name, SessionState Session)> cases = [];
        foreach ((string name, (int Copies, int Draw)[] effects) in new (string, (int, int)[])[] {
            ("no-draw", []), ("one-draw2", [(1, 2)]), ("three-copies-draw2", [(3, 2)]),
            ("three-identities", [(3, 2), (3, 2), (3, 2)]), ("mixed-123", [(3, 1), (3, 2), (3, 3)])
        }) {
            List<Card> cards = [new([starter], 9, id: "starter"), new([extender], 9, id: "extender"),
                new([], 22 - effects.Sum(effect => effect.Copies), id: "blank")];
            for (int i = 0; i < effects.Length; i++) {
                cards.Add(new([starter, extender], effects[i].Copies, id: $"draw{i}", drawCount: effects[i].Draw));
            }

            cases.Add((name, new() { Cards = cards, Combos = combos, ComboGroups = groups, HandSize = 5 }));
            if (effects.Length > 0) {
                cases.Add(($"{name}-unlimited", new() {
                    Cards = cards.Select(card => card.WithDrawOncePerTurn(false)).ToList(),
                    Combos = combos, ComboGroups = groups, HandSize = 5
                }));
            }
        }

        SessionService codec = new(Mock.Of<IJSRuntime>(), new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());
        string exampleJson = File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "example_session_state.json"));
        SessionState example = await codec.LoadSessionAsync(exampleJson);
        cases.Add(("example-baseline", example));
        foreach (bool once in new[] { true, false }) {
            SessionState fuwalos = await codec.LoadSessionAsync(exampleJson);
            int index = fuwalos.Cards.FindIndex(card => card.Name == "Mulcharmy Fuwalos");
            Assert.That(index, Is.GreaterThanOrEqualTo(0));
            Assert.That(fuwalos.Cards[index].Copies, Is.EqualTo(3));
            fuwalos.Cards[index] = fuwalos.Cards[index].WithDrawCount(3).WithDrawOncePerTurn(once);
            cases.Add(($"example-fuwalos-draw3-{(once ? "once" : "unlimited")}", fuwalos));
        }
        SessionState configured = await codec.LoadSessionAsync(exampleJson);
        for (int i = 0; i < 3; i++) {
            configured.Cards[i] = configured.Cards[i].WithDrawCount(i + 1);
        }

        cases.Add(("example-mixed-123", configured));
        SessionState unrestricted = await codec.LoadSessionAsync(codec.SerializeSession(configured));
        for (int i = 0; i < unrestricted.Cards.Count; i++) {
            unrestricted.Cards[i] = unrestricted.Cards[i].WithDrawOncePerTurn(false);
        }
        cases.Add(("example-mixed-123-unlimited", unrestricted));
        SessionState stress = new() {
            Cards = unrestricted.Cards, Combos = unrestricted.Combos, ComboGroups = unrestricted.ComboGroups, HandSize = 9
        };
        cases.Add(("example-mixed-123-unlimited-hand9", stress));
        // Restrictive maxima defeat role factoring. Probe both expensive counting
        // and early union-storage rejection after the optimization.
        foreach (int routes in new[] { 15, 22 }) {
            CategoryBase[] roles = Enumerable.Range(0, routes).Select(index => new CategoryBase($"Role{index}")).ToArray();
            List<Card> cards = roles.Select((role, index) => new Card([role], 2, id: $"role{index}")).ToList();
            cards.Add(new([], 57 - routes * 2, id: "blank"));
            cards.Add(new([], 3, id: "draw", drawCount: 3, drawOncePerTurn: false));
            cases.Add(($"restrictive-{routes}-draw3-unlimited", new() {
                Cards = cards, Combos = roles.Select(role => new Combo([new(role, 1, 1)])).ToList(), HandSize = 5
            }));
        }
        List<object> exported = [];
        foreach ((string name, SessionState session) in cases) {
            List<Card> deck = session.Cards.Where(card => card.Active).ToList();
            List<Combo> activeCombos = session.Combos.Where(combo => combo.Active).ToList();
            List<Card> effects = deck.Where(card => card.DrawCount is not null).ToList();
            int ordinary = deck.Where(card => card.DrawCount is null).Sum(card => card.Copies);
            string? allowance = Environment.GetEnvironmentVariable("YGO_DRAW_BENCHMARK_WORK");
            CalculationWorkPolicy policy = allowance is null ? CalculationWorkPolicy.Interactive : new(long.Parse(allowance));
            WorkBudget resolutionBudget = new(policy);
            Stopwatch resolutionTimer = Stopwatch.StartNew();
            DrawResolution resolution = DrawEffectResolver.Resolve(ordinary, effects, session.HandSize, resolutionBudget);
            resolutionTimer.Stop();
            WorkBudget budget = new(policy);
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            Stopwatch timer = Stopwatch.StartNew();
            string outcome = "Success";
            double? probability = null;
            ProbabilityCalculationResult? referenceResults = null;
            ExactCalculationResult? exact = null;
            try {
                exact = new DrawEffectCalculation(deck, session.HandSize, budget).Calculate(activeCombos, session.ComboGroups);
                referenceResults = exact.ToPublic(activeCombos, session.ComboGroups, budget);
                probability = referenceResults.TotalProbability;
            }
            catch (ProbabilityCalculationLimitException exception) {
                outcome = exception.Reason.ToString();
            }

            timer.Stop();
            allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            CalculationSnapshot snapshot = CalculationSnapshot.Capture(deck, activeCombos, session.HandSize, session.ComboGroups, policy);
            exported.Add(new { Name = name, Scenarios = resolution.Scenarios.Count, Work = budget.Spent, Outcome = outcome,
                Milliseconds = timer.Elapsed.TotalMilliseconds, AllocatedBytes = allocated,
                ResolverWork = resolutionBudget.Spent, ResolverMilliseconds = resolutionTimer.Elapsed.TotalMilliseconds,
                ReferenceResults = referenceResults,
                ExactResults = exact is null ? null : new[] { exact.Total }.Concat(exact.Combos).Concat(exact.Groups)
                    .Select(value => $"{value.Numerator}/{value.Denominator}").ToArray(),
                Probability = probability, Input = JsonSerializer.Deserialize<JsonElement>(snapshot.Json) });
            TestContext.Out.WriteLine($"{name}: scenarios={resolution.Scenarios.Count}; resolverWork={resolutionBudget.Spent}; work={budget.Spent}; {outcome}; p={probability:R}; ms={timer.Elapsed.TotalMilliseconds:F2}; allocated={allocated}");
        }

        string? directory = Environment.GetEnvironmentVariable("YGO_DRAW_BENCHMARK_OUTPUT");
        if (!string.IsNullOrEmpty(directory)) {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "draw-worker-cases.json"), JsonSerializer.Serialize(exported));
        }
    }
}
