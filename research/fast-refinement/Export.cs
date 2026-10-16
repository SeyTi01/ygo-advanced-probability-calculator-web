using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.Session;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture, NonParallelizable]
public sealed class RefinementResearchExport {
    [Test, Explicit("Research corpus export, never production")]
    public async Task Export() {
        string destination = Environment.GetEnvironmentVariable("YGO_RESEARCH")!;
        List<(string Name, string Split, string Kind, SessionState State)> cases = [];
        SessionState example = await SearchAvailabilityCertificateTest.Example("plain");
        SessionState desires = await SearchAvailabilityCertificateTest.Example("plain");
        int index = desires.Cards.FindIndex(card => card.Name == "Mulcharmy Fuwalos");
        desires.Cards[index] = new([], 3, "Pot of Desires", id: "research-desires", externalCardId: 35261759,
            drawCount: 2, preDrawDeckRemovalCount: 10);
        cases.Add(("vsk9-three-desires", "design", "maintained-equivalent", desires));
        cases.Add(("redundant-two", "design", "synthetic-normal", Synthetic(0)));
        cases.Add(("overlap-or", "design", "synthetic-normal", Synthetic(1)));
        cases.Add(("multiple-draw-sources", "design", "synthetic-normal", Synthetic(2)));
        cases.Add(("maxima-exclusion", "held-out", "synthetic-normal", Synthetic(3)));
        cases.Add(("direct-plus-category", "held-out", "synthetic-normal", Synthetic(4)));
        foreach (string name in new[] { "ordered-target-loss-40", "deferred-target-loss-40" }) {
            // Exported existing fixtures are loaded below; no solver is run during freeze.
            cases.Add((name, "held-out", "maintained-synthetic", new()));
        }
        cases.Add(("vsk9-no-desires", "control", "maintained", example));
        SessionState noSearch = await SearchAvailabilityCertificateTest.Example("mill2");
        for (int row = 0; row < noSearch.Cards.Count; row++) { noSearch.Cards[row] = noSearch.Cards[row].WithSearch(null); }
        cases.Add(("no-search-draw", "control", "maintained-synthetic", noSearch));
        SessionState artificial = TargetSurvivalCertificateTest.Motivating();
        cases.Add(("artificial-32-search", "diagnostic", "artificial", artificial));
        SessionState blanks = TargetSurvivalCertificateTest.Motivating();
        blanks.Cards[2] = blanks.Cards[2].WithCopies(30);
        blanks.Cards.Add(new([], 2, "Blank", id: "blank"));
        cases.Add(("artificial-30-search", "diagnostic", "artificial", blanks));
        SessionService codec = new(null!, new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());
        JsonElement[] old = JsonSerializer.Deserialize<JsonElement[]>(File.ReadAllText(Path.Combine(destination, "fast-export.json")))!;
        List<object> output = [];
        foreach ((string name, string split, string kind, SessionState initial) in cases) {
            SessionState state = initial;
            if (state.Cards.Count == 0) {
                state = await codec.LoadSessionAsync(old.Single(item => item.GetProperty("Name").GetString() == name).GetProperty("Session").GetRawText());
                // Split pooled irrelevant filler into <=3-copy identities; preserve all roles/plans.
                Card filler = state.Cards.Single(card => card.Id == "f");
                state.Cards.Remove(filler);
                for (int left = filler.Copies, row = 0; left > 0; row++) {
                    int copies = Math.Min(3, left);
                    state.Cards.Add(new([], copies, "Filler " + row, id: "f-" + row));
                    left -= copies;
                }
            }
            Add(name, split, kind, state, CalculationModel.Fast, CalculationWorkPolicy.Interactive);
        }
        Add("opening-control", "control", "maintained", desires, CalculationModel.OpeningHand, CalculationWorkPolicy.Interactive);
        Add("work-limited", "control", "maintained-equivalent", desires, CalculationModel.Fast, new(1000));
        File.WriteAllText(Path.Combine(destination, "corpus.json"), JsonSerializer.Serialize(output));

        void Add(string name, string split, string kind, SessionState state, CalculationModel mode, CalculationWorkPolicy work) {
            string json = CalculationSnapshot.Capture(state.Cards, state.Combos, state.HandSize, state.ComboGroups,
                work, options: new(state.DrawActivationPriority, Mode: mode)).Json;
            File.WriteAllText(Path.Combine(destination, name + ".input.json"), json);
            File.WriteAllText(Path.Combine(destination, name + ".session.json"), codec.SerializeSession(state));
            output.Add(new { Name = name, Split = split, Kind = kind, Input = JsonSerializer.Deserialize<JsonElement>(json) });
        }
    }

    private static SessionState Synthetic(int variant) {
        CategoryBase a = new("A");
        CategoryBase b = new("B");
        List<Card> cards = [new([a, b], 3, "A and B", id: "ab"), new([a], 3, "A", id: "a"),
            new([b], 3, "B", id: "b"), new([], 3, "Search A", id: "sa", search: new(CardSelector.For(a), false)),
            new([], 2, "Search B", id: "sb", search: new(CardSelector.For(b), false)),
            new([], 3, "Desires", id: "d", drawCount: 2, preDrawDeckRemovalCount: 10)];
        if (variant == 2) { cards.Add(new([], 1, "Mill draw", id: "m", drawCount: 1, postDrawDeckMillCount: 2)); }
        int left = 40 - cards.Sum(card => card.Copies);
        for (int row = 0; left > 0; row++) {
            int copies = Math.Min(3, left);
            cards.Add(new([], copies, "Filler " + row, id: "f" + row));
            left -= copies;
        }
        List<Combo> combos = variant switch {
            0 => [new([new(a, 2, 5)], "Two A", groupId: "g"), new([new(b, 2, 5)], "Two B", groupId: "g")],
            1 => [new([new(a, 1, 5), new(b, 1, 5)], "Distinct overlapping roles", groupId: "g"),
                new([], "OR route", groupId: "g", alternativeGroups: [new([ComboAlternative.For(new ComboCard("a", 2, 5)), ComboAlternative.For(new ComboCard("b", 2, 5))])])],
            3 => [new([new(a, 1, 2)], "Bounded A", groupId: "g", cards: [new("ab", 0, 0)]),
                new([new(b, 2, 3)], "Bounded B", groupId: "g")],
            4 => [new([new(a, 1, 5)], "Direct plus category", groupId: "g", cards: [new("ab", 1, 5)]),
                new([new(b, 2, 5)], "B route", groupId: "g", cards: [new("a", 1, 5)])],
            _ => [new([new(a, 2, 5)], "Two A", groupId: "g"), new([new(a, 1, 5), new(b, 1, 5)], "A plus B", groupId: "g")]
        };
        return new() { Cards = cards, Categories = [a, b], HandSize = 5, Combos = combos, ComboGroups = [new("g", "Routes")] };
    }
}
