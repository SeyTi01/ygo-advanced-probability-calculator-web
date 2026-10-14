using Bunit;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.DeckImport;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.Session;
using YGOProbabilityCalculatorBlazorTest.Services.Session;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public class TreatedNameIdentityTest {
    private static readonly CategoryBase Umi = new("Archetype: Umi", CategorySource.Metadata, "archetype:umi");

    [TestCase("Umi", 0), TestCase("Lemuria, the Forgotten City", 0)]
    [TestCase("Umi", 1), TestCase("Lemuria, the Forgotten City", 1)]
    [TestCase("Umi", 2), TestCase("Lemuria, the Forgotten City", 2)]
    public async Task SharedTreatedNameNeverSubstitutesForSelectedEntryBeforeOrAfterReload(string selectedName, int mode) {
        IReadOnlyList<CardInfo> catalog = CatalogArchetypeReconcilerTest.BundledCatalog();
        List<Card> deck = new[] { "Umi", "Lemuria, the Forgotten City" }.Select(name => {
            CardInfo info = catalog.Single(card => card.Name == name);
            return CuratedDrawEffectPresets.CreateImportedCard(info, 1, info.Id);
        }).ToList();
        Assert.That(deck.Select(card => card.Id).Distinct().Count(), Is.EqualTo(2));
        Assert.That(deck.All(card => card.Categories.Any(category => category.Identity == Umi.Identity)), Is.True);
        string selectedId = deck.Single(card => card.Name == selectedName).Id;
        List<Combo> combos = [new([], "Selected entry", cards: [new(selectedId, 1, 1)]), new([new(Umi, 1, 2)], "Shared Umi")];
        if (mode > 0) {
            deck.Add(new([], name: "Draw source", id: "draw-source", drawCount: mode,
                postDrawDiscardCount: mode == 2 ? 1 : 0));
        }
        SessionState session = new() { Cards = deck, Combos = combos, HandSize = 1, DrawEffectsEnabled = mode > 0 };
        using Bunit.TestContext context = new();
        SessionService sessions = new(context.JSInterop.JSRuntime, new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());
        string saved = sessions.SerializeSession(session);
        string link = SessionShareCodec.CreateLink("https://calculator.test/", saved);
        SessionState shared = await sessions.LoadSessionAsync(SessionShareCodec.Decode(link[link.IndexOf('#')..]));
        SessionState recovered = await sessions.LoadSessionAsync(sessions.SerializeSession(shared));
        CatalogArchetypeReconciler.Reconcile(recovered.Cards, catalog);
        Assert.That(sessions.SerializeSession(recovered), Is.EqualTo(saved));
        Assert.That(recovered.Combos[0].Cards.Single().CardId, Is.EqualTo(selectedId));

        foreach (SessionState state in new[] { session, recovered }) {
            foreach (bool configured in new[] { false, true }) {
                List<Card> modeled = configured ? state.Cards : state.Cards.Select(card => card.WithPostDrawDiscardCount(0)).ToList();
                // Compare each requirement as its own calculation so the active
                // route set has the same removal priorities in production/oracle.
                for (int index = 0; index < state.Combos.Count; index++) {
                    List<Combo> requirements = [state.Combos[index]];
                    double expected = (index, mode) switch {
                        (1, _) => 1,
                        (_, 2) => 2d / 3,
                        _ => 0.5
                    };
                    ProbabilityCalculationResult result = CalculationWire.ReadResult(CalculationWire.Execute(
                        CalculationSnapshot.Capture(state.Cards, requirements, 1, [],
                            drawEffectsEnabled: mode > 0, options: new()).Json));
                    Assert.That(result.TotalProbability, Is.EqualTo(expected).Within(1e-14));
                    Assert.That(result.ComboProbabilities.Single().Probability, Is.EqualTo(expected).Within(1e-14));
                    if (mode == 0) {
                        Assert.That(SmallDeckOracle.EnumerateProbability(modeled, requirements, 1), Is.EqualTo(expected));
                    } else {
                        DrawSequenceOracle.Counts oracle = DrawSequenceOracle.Enumerate(modeled, 1, requirements, []);
                        ExactCalculationResult exact = new DrawEffectCalculation(state.Cards, 1,
                            new(CalculationWorkPolicy.Interactive)).Calculate(requirements, []);
                        Assert.That(exact.Combos[0].Numerator * oracle.Total,
                            Is.EqualTo(oracle.Wins[1] * exact.Combos[0].Denominator));
                    }
                }
            }
        }
    }
}
