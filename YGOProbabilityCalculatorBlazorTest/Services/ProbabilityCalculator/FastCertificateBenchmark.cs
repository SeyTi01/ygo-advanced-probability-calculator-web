using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.Session;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture, Explicit("Paired Fast certificate native/WASM input export"), NonParallelizable]
public sealed class FastCertificateBenchmark {
    [Test]
    public async Task Export() {
        List<(string Name, SessionState State, bool Physical)> cases = [];
        cases.Add(("multi-target-mill-40", KnownSourceLossCertificateTest.TwoTargetForty(), true));
        SessionState ordinary = await SearchAvailabilityCertificateTest.Example("plain");
        for (int row = 0; row < ordinary.Cards.Count; row++) { ordinary.Cards[row] = ordinary.Cards[row].WithSearch(null); }
        cases.Add(("ordinary-40", ordinary, false));
        SessionState count = await SearchAvailabilityCertificateTest.Example("one-draw2");
        for (int row = 0; row < count.Cards.Count; row++) { count.Cards[row] = count.Cards[row].WithSearch(null); }
        cases.Add(("no-search-draw2-40", count, false));
        foreach (string name in new[] { "plain", "one-draw2", "banish10", "mill2" }) {
            cases.Add((name + "-40", await SearchAvailabilityCertificateTest.Example(name), false));
        }
        foreach (string name in new[] { "known-source-two-targets", "draw-target-source-consumed",
            "known-source-opt-False-searchable-True-hidden-1", "minimal-filtered-target-discard", "coalesced-uneven-targets", "mixed-visible-hidden",
            "deferred-loss-cost-True-first-False", "ordered-pooling-3-reversed-False" }) {
            SearchCertificateFixtures.Fixture fixture = SearchCertificateFixtures.Fixtures().Single(value => value.Name == name);
            cases.Add((name, new() { Cards = fixture.Deck, HandSize = fixture.Hand, Combos = fixture.Combos, ComboGroups = fixture.Groups,
                DrawActivationPriority = fixture.Priority is null ? new() : new(DrawActivationMode.CustomPriority, fixture.Priority) }, true));
        }
        CategoryBase target = new("Target");
        CategoryBase other = new("Other");
        CategoryBase material = new("Material");
        foreach (bool deferred in new[] { false, true }) {
            cases.Add((deferred ? "deferred-target-loss-40" : "ordered-target-loss-40", new() {
                Cards = [new([], 2, id: "s", search: new(CardSelector.For(target), false)),
                    new([target], 3, id: "t"), new([other], 3, id: "b"), new([material], 2, id: "m"), new([], 28, id: "f"),
                    new([], 1, id: "d", drawCount: 2, postDrawDiscardCount: 1,
                        postDrawSelector: CardSelector.For(deferred ? material : target), postDrawDeckMillCount: 2),
                    new([], 1, id: "e", drawCount: 1, firstActivationOnly: !deferred)],
                HandSize = 5, Categories = [target, other, material],
                Combos = [new([new(target, 1, 2)], "One or two targets", groupId: "g"),
                    new([new(target, 1, 5), new(other, 1, 5)], "Distinct target and other", groupId: "g")],
                ComboGroups = [new("g", "Routes")]
            }, false));
        }
        SessionState large = await SearchAvailabilityCertificateTest.Example("mill2");
        for (int row = 0; row < large.Cards.Count; row++) {
            if (large.Cards[row].DrawCount is not null) { large.Cards[row] = large.Cards[row].WithPostDrawDiscardCount(1); }
        }
        cases.Add(("vs-k9-ordered-40", large, false));
        List<object> output = [];
        SessionService codec = new(null!, new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());
        foreach ((string name, SessionState state, bool physical) in cases) {
            output.Add(new { Name = name, Physical = physical,
                Input = JsonSerializer.Deserialize<JsonElement>(CalculationSnapshot.Capture(state.Cards, state.Combos, state.HandSize, state.ComboGroups,
                    CalculationWorkPolicy.Interactive, options: new(state.DrawActivationPriority)).Json),
                Session = JsonSerializer.Deserialize<JsonElement>(codec.SerializeSession(state)) });
        }
        string? coveragePath = Environment.GetEnvironmentVariable("YGO_FAST_COVERAGE_FIXTURES");
        if (coveragePath is not null) {
            File.WriteAllText(coveragePath, JsonSerializer.Serialize(SearchCertificateFixtures.Fixtures().Select(fixture => new {
                Name = fixture.Name,
                Input = JsonSerializer.Deserialize<JsonElement>(CalculationSnapshot.Capture(fixture.Deck, fixture.Combos, fixture.Hand, fixture.Groups,
                    options: new(fixture.Priority is null ? null : new(DrawActivationMode.CustomPriority, fixture.Priority))).Json)
            })));
        }
        File.WriteAllText(Environment.GetEnvironmentVariable("YGO_FAST_CERTIFICATE_FIXTURES")
            ?? throw new InvalidOperationException("Set YGO_FAST_CERTIFICATE_FIXTURES."), JsonSerializer.Serialize(output));
    }
}
