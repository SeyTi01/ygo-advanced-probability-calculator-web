using Microsoft.JSInterop;
using Moq;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.Session;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

// Opt in: dotnet test YGOProbabilityCalculatorBlazor.sln -c Release --filter FullyQualifiedName~UnionBenchmark --logger "console;verbosity=detailed"
// Keep expensive physical-hand enumeration out of ordinary test runs.
[TestFixture, Explicit("Full physical-hand exact-union oracle")]
public class UnionBenchmark {
    internal static async Task<SessionState> LoadModel() {
        string json = await File.ReadAllTextAsync(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "vsmodel.json"));
        return await new SessionService(Mock.Of<IJSRuntime>(),
            new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer()).LoadSessionAsync(json);
    }

    [Test]
    public async Task SuppliedModelMatchesEveryPhysicalHand() {
        SessionState session = await LoadModel();
        List<Card> deck = session.Cards.Where(c => c.Active).ToList();
        List<Combo> combos = session.Combos.Where(c => c.Active).ToList();
        Func<IReadOnlyList<Card>, bool>[] predicates = combos.Select(combo => SmallDeckOracle.HandPredicate(combo, session.HandSize)).ToArray();
        Card[] copies = deck.SelectMany(c => Enumerable.Repeat(c, c.Copies)).ToArray();
        List<Card> hand = [];
        long total = 0, union = 0;
        long[] individual = new long[combos.Count];
        long[] grouped = new long[session.ComboGroups.Count];
        bool[] matches = new bool[combos.Count];
        Visit(0);
        TestContext.Out.WriteLine($"Exact counts: denominator={total}, total={union}, groups=[{string.Join(",", grouped)}], individual=[{string.Join(",", individual)}]");
        Assert.That(total, Is.EqualTo(658008));
        ProbabilityCalculationResult actual = new ProbabilityCalculatorService().CalculateProbabilityResults(deck, combos, session.HandSize, session.ComboGroups);
        Assert.That(actual.TotalProbability, Is.EqualTo((double)union / total).Within(1e-12));
        for (int i = 0; i < combos.Count; i++) {
            Assert.That(actual.ComboProbabilities[i].Probability, Is.EqualTo((double)individual[i] / total).Within(1e-12));
        }

        for (int i = 0; i < grouped.Length; i++) {
            Assert.That(actual.GroupProbabilities![i].Probability, Is.EqualTo((double)grouped[i] / total).Within(1e-12));
        }

        void Visit(int start) {
            if (hand.Count == session.HandSize) {
                total++;
                for (int i = 0; i < matches.Length; i++) {
                    matches[i] = predicates[i](hand);

                    if (matches[i]) {
                        individual[i]++;
                    }
                }

                if (matches.Any(m => m)) {
                    union++;
                }

                for (int g = 0; g < grouped.Length; g++) {
                    if (Enumerable.Range(0, combos.Count).Any(i => matches[i] && combos[i].GroupId == session.ComboGroups[g].Id)) {
                        grouped[g]++;
                    }
                }

                return;
            }

            for (int i = start; i <= copies.Length - (session.HandSize - hand.Count); i++) {
                hand.Add(copies[i]);
                Visit(i + 1);
                hand.RemoveAt(hand.Count - 1);
            }
        }
    }

}
