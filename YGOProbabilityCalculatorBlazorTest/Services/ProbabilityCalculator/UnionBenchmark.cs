using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.JSInterop;
using Moq;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.Session;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

// Opt in: dotnet test YGOProbabilityCalculatorBlazor.sln -c Release --filter FullyQualifiedName~UnionBenchmark --logger "console;verbosity=detailed"
// For stable native comparisons, set DOTNET_TieredCompilation=0 for both revisions.
// Keep expensive physical-hand enumeration and timing out of ordinary test runs.
[TestFixture, Explicit("Reproducible exact-union benchmark and full physical-hand oracle")]
public class UnionBenchmark {
    internal static async Task<SessionState> LoadModel() {
        var json = await File.ReadAllTextAsync(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "vsmodel.json"));
        return await new SessionService(Mock.Of<IJSRuntime>(),
            new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer()).LoadSessionAsync(json);
    }

    [Test]
    public async Task SuppliedModelMatchesEveryPhysicalHand() {
        var session = await LoadModel();
        var deck = session.Cards.Where(c => c.Active).ToList();
        var combos = session.Combos.Where(c => c.Active).ToList();
        var predicates = combos.Select(SmallDeckOracleTest.HandPredicate).ToArray();
        var copies = deck.SelectMany(c => Enumerable.Repeat(c, c.Copies)).ToArray();
        var hand = new List<Card>();
        long total = 0, union = 0;
        var individual = new long[combos.Count];
        var grouped = new long[session.ComboGroups.Count];
        var matches = new bool[combos.Count];
        Visit(0);
        TestContext.Out.WriteLine($"Exact counts: denominator={total}, total={union}, groups=[{string.Join(",", grouped)}], individual=[{string.Join(",", individual)}]");
        Assert.That(total, Is.EqualTo(658008));
        var actual = new ProbabilityCalculatorService().CalculateProbabilityResults(deck, combos, session.HandSize, session.ComboGroups);
        Assert.That(actual.TotalProbability, Is.EqualTo((double)union / total).Within(1e-12));
        for (var i = 0; i < combos.Count; i++)
            Assert.That(actual.ComboProbabilities[i].Probability, Is.EqualTo((double)individual[i] / total).Within(1e-12));
        for (var i = 0; i < grouped.Length; i++)
            Assert.That(actual.GroupProbabilities![i].Probability, Is.EqualTo((double)grouped[i] / total).Within(1e-12));

        void Visit(int start) {
            if (hand.Count == session.HandSize) {
                total++;
                for (var i = 0; i < matches.Length; i++) {
                    matches[i] = predicates[i](hand);
                    if (matches[i]) individual[i]++;
                }
                if (matches.Any(m => m)) union++;
                for (var g = 0; g < grouped.Length; g++)
                    if (Enumerable.Range(0, combos.Count).Any(i => matches[i] && combos[i].GroupId == session.ComboGroups[g].Id)) grouped[g]++;
                return;
            }
            for (var i = start; i <= copies.Length - (session.HandSize - hand.Count); i++) {
                hand.Add(copies[i]);
                Visit(i + 1);
                hand.RemoveAt(hand.Count - 1);
            }
        }
    }

    [Test]
    public async Task MeasureFixedWorkloads() {
        TestContext.Out.WriteLine($"{RuntimeInformation.FrameworkDescription}; {RuntimeInformation.OSDescription}; {RuntimeInformation.ProcessArchitecture}; CPUs={Environment.ProcessorCount}");
        var session = await LoadModel();
        var deck = session.Cards.Where(c => c.Active).ToList();
        var combos = session.Combos.Where(c => c.Active).ToList();
        Measure("VS/K9-10", deck, combos, 5, session.ComboGroups);
        Measure("VS/K9-30-duplicates", deck, Enumerable.Range(0, 30).Select(i => combos[i % 10]).ToList(), 5, session.ComboGroups);
        foreach (var count in new[] { 10, 15, 20, 30 }) {
            var alternatives = deck.Take(count - 5).Select((c, i) => new Combo([new(session.Categories[0], 1, 5)],
                groupId: session.ComboGroups[i % 2].Id, cards: [new(c.Id, 1, 5)])).ToList();
            alternatives.AddRange(combos.Take(5));
            Measure($"VS/K9-{alternatives.Count}-alternatives", deck, alternatives, 5, session.ComboGroups);
        }
        foreach (var count in new[] { 6, 10, 18 }) {
            var categories = Enumerable.Range(0, count).Select(i => new CategoryBase($"Distinct{i}")).ToArray();
            var distinctDeck = categories.Select(c => new Card([c], 2)).Append(new Card([], 60 - count * 2)).ToList();
            // Min=2 alternatives cannot be replaced by two copies from a union:
            // drawing one copy from each of two selectors must still fail.
            var distinct = categories.Select((c, i) => new Combo([new(c, 2, 2)], groupId: $"g{i % 2}")).ToList();
            Measure($"distinct-restrictive-{count}", distinctDeck, distinct, 5, [new("g0", "Even"), new("g1", "Odd")]);
            if (count == 18)
                Measure("distinct-zero-max-18", distinctDeck,
                    categories.Select(c => new Combo([new(c, 0, 0)])).ToList(), 5, []);
        }
    }

    private static void Measure(string name, List<Card> deck, List<Combo> combos, int hand, List<ComboGroup> groups) {
        var service = new ProbabilityCalculatorService();
        var cold = Run();
        for (var i = 0; i < 3; i++) Run();
        var trials = Enumerable.Range(0, 7).Select(_ => Run()).ToArray();
        var times = trials.Select(t => t.Ms).Order().ToArray();
        TestContext.Out.WriteLine($"{name}: cold={cold.Ms:F3}ms; median={times[3]:F3}ms range={times[0]:F3}..{times[^1]:F3}; bytes={trials[0].Bytes}; {trials[0].Outcome}");

        (double Ms, long Bytes, string Outcome) Run() {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var timer = Stopwatch.StartNew();
            string outcome;
            try {
                var result = service.CalculateProbabilityResults(deck, combos, hand, groups);
                outcome = $"p={result.TotalProbability:R}";
            } catch (ProbabilityCalculationLimitException) { outcome = "explicit work/storage limit"; }
            timer.Stop();
            return (timer.Elapsed.TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - before, outcome);
        }
    }
}
