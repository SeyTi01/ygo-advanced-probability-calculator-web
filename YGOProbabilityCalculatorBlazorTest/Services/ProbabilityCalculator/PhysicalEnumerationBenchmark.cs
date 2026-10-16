using System.Text.Json;
using System.Text.Json.Nodes;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.Session;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture, Explicit("Frozen exact fallback benchmark inputs; not timing assertions"), NonParallelizable]
public sealed class PhysicalEnumerationBenchmark {
    [Test]
    public async Task Export() {
        string file = Environment.GetEnvironmentVariable("YGO_ENUMERATION_FIXTURES") ?? throw new InvalidOperationException("Set YGO_ENUMERATION_FIXTURES.");
        List<(string Name, SessionState State, CalculationModel Mode, bool Unchanged)> cases = [];
        foreach (string name in new[] { "minimal-filtered-target-discard", "coalesced-uneven-targets", "mixed-visible-hidden", "solar-style-cost-and-mill", "multiple-draw-opt-duplicates" }) {
            SearchCertificateFixtures.Fixture fixture = SearchCertificateFixtures.Fixtures().Single(value => value.Name == name);
            cases.Add((name, new() { Cards = fixture.Deck, HandSize = fixture.Hand, Combos = fixture.Combos, ComboGroups = fixture.Groups,
                DrawActivationPriority = fixture.Priority is null ? new() : new(DrawActivationMode.CustomPriority, fixture.Priority) }, CalculationModel.Exact, false));
        }
        CategoryBase target = new("Target");
        foreach (int size in new[] { 12, 40 }) {
            cases.Add(($"ordered-{size}", new() {
                Cards = [new([], 2, id: "s", search: new(CardSelector.For(target), false)), new([target], 3, id: "a"), new([], size - 7, id: "f"),
                    new([], 1, id: "d", drawCount: 2, postDrawDiscardCount: 1, postDrawSelector: CardSelector.For(target), postDrawDeckMillCount: 2),
                    new([], 1, id: "e", drawCount: 1, firstActivationOnly: true)],
                HandSize = size == 12 ? 3 : 5,
                Combos = [new([new(target, 1, 2)], "One or two targets"), new([new(target, 0, 0)], "No target")]
            }, CalculationModel.Exact, false));
        }
        foreach (int rows in new[] { 8, 9 }) {
            List<Card> edge = Enumerable.Range(0, rows - 3).Select(index => new Card([target], 3, id: "t" + index)).ToList();
            edge.Add(new([], 2, id: "s", search: new(CardSelector.For(target), false)));
            edge.Add(new([], 2, id: "d", drawCount: 2, drawOncePerTurn: false, postDrawDiscardCount: 1, postDrawDeckMillCount: 1));
            edge.Add(new([], 40 - edge.Sum(card => card.Copies), id: "f"));
            cases.Add(($"selector-edge-{rows}", new() { Cards = edge, HandSize = 5,
                Combos = [new([new(target, 1, 2)], "One or two targets"), new([], "Direct", cards: [new("t0", 1, 5)])]
            }, CalculationModel.Exact, false));
        }
        SessionState hard = await SearchAvailabilityCertificateTest.Example("mill2");
        for (int row = 0; row < hard.Cards.Count; row++) {
            Card card = hard.Cards[row];
            if (card.DrawCount is not null) {
                hard.Cards[row] = new(card.Categories, card.Copies, card.Name, id: card.Id, drawCount: card.DrawCount,
                    postDrawDiscardCount: 1, postDrawDeckMillCount: card.PostDrawDeckMillCount);
            }
        }
        cases.Add(("vs-k9-ordered-40", hard, CalculationModel.Exact, false));
        SessionState example = await SearchAvailabilityCertificateTest.Example("one-draw2");
        cases.Add(("opening", example, CalculationModel.OpeningHand, true));
        cases.Add(("fast-search", example, CalculationModel.Fast, true));
        cases.Add(("physical-mill2", await SearchAvailabilityCertificateTest.Example("mill2"), CalculationModel.Exact, true));
        cases.Add(("physical-banish10", await SearchAvailabilityCertificateTest.Example("banish10"), CalculationModel.Exact, true));
        SessionState noSearch = await SearchAvailabilityCertificateTest.Example("one-draw2");
        for (int row = 0; row < noSearch.Cards.Count; row++) {
            noSearch.Cards[row] = noSearch.Cards[row].WithSearch(null);
        }
        cases.Add(("no-search", noSearch, CalculationModel.Fast, true));
        SessionState discard = new() { Cards = ThreeCalculationModesTest.Counterexample("discard"), HandSize = 2,
            Combos = ThreeCalculationModesTest.Combos, ComboGroups = ThreeCalculationModesTest.Groups };
        cases.Add(("fast-discard", discard, CalculationModel.Fast, true));
        List<object> output = [];
        SessionService codec = new(null!, new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());
        foreach ((string name, SessionState state, CalculationModel mode, bool unchanged) in cases) {
            JsonObject input = JsonNode.Parse(CalculationSnapshot.Capture(state.Cards, state.Combos, state.HandSize, state.ComboGroups,
                CalculationWorkPolicy.Default, options: new(state.DrawActivationPriority, Mode: mode)).Json)!.AsObject();
            JsonObject session = JsonNode.Parse(codec.SerializeSession(state))!.AsObject();
            session["CalculationModel"] = mode.ToString();
            output.Add(new { Name = name, Input = input, BaselineInput = input, Session = session, Unchanged = unchanged });
        }
        File.WriteAllText(file, JsonSerializer.Serialize(output));
    }
}
