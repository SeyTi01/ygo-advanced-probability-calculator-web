using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;
using YGOProbabilityCalculatorBlazor.Services.DeckImport;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.Session;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture, Explicit("Frozen post-221 native/WASM performance matrix"), NonParallelizable]
public sealed class BoundedSearchBenchmark {
    [Test]
    public async Task Export() {
        string root = TestContext.CurrentContext.TestDirectory;
        while (!File.Exists(Path.Combine(root, "YGOProbabilityCalculatorBlazor", "wwwroot", "data", "card-catalog.v1.json"))) {
            root = Directory.GetParent(root)!.FullName;
        }
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "YGOProbabilityCalculatorBlazor", "wwwroot", "data", "card-catalog.v1.json")));
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web);
        CardInfo[] catalog = document.RootElement.GetProperty("cards").EnumerateArray()
            .Select(value => value.Deserialize<CardInfo>(options)!).OrderBy(card => card.Id).ToArray();
        Card Import(CardInfo info, int copies, string id) => new Card(CardPropertyProvider.GetCategories(info), copies,
            info.Name, id: id, externalCardId: info.Id).WithEffect(
                CardEffectRegistry.Default.Find(info)?.Plan ?? new Card([]).EffectPlan,
                CardEffectRegistry.Default.Find(info)?.Profile ?? EffectProfile.Legacy);
        List<(string Name, SessionState State)> cases = [];
        foreach ((int identity, string name, int targets, int sources, bool distinct, int hand) in new[] {
            (36637374, "branded-one", 1, 1, false, 5),
            (36637374, "branded-many", 3, 1, false, 5),
            (36637374, "branded-distinct", 3, 1, true, 5),
            (36637374, "branded-six", 3, 1, false, 6),
            (98567237, "tract-many", 3, 1, false, 5),
            (98567237, "tract-distinct", 3, 1, true, 5),
            (67115133, "chant-one", 3, 1, false, 5),
            (67115133, "chant-two", 3, 2, false, 5),
            (67115133, "chant-three", 3, 3, false, 5),
            (78058681, "kewl-three", 3, 3, false, 5),
            (6153210, "ketu-control", 1, 1, false, 5),
            (60764609, "engraver-control", 1, 1, false, 5)
        }) {
            Card source = Import(catalog.Single(info => info.Id == identity), sources, "source");
            Card[] candidates = catalog.Where(info => info.Id != identity)
                .Select(info => Import(info, 1, "target-" + info.Id)).Where(card => source.Search!.Matches(card)).ToArray();
            Card[] selected = source.Search!.Modes is { } modes
                ? modes.SelectMany(mode => candidates.Where(card => source.Search.Matches(card, mode.Selector)).Take(targets))
                    .DistinctBy(card => card.Id).ToArray()
                : candidates.Take(targets).ToArray();
            // Target effects are intentionally retained: fetched effects must stay inert.
            List<Card> cards = [source, .. selected];
            CategoryBase other = new("Other role");
            cards.Add(new([other], 3, "Other role A", id: "other-a"));
            cards.Add(new([other], 2, "Other role B", id: "other-b"));
            int left = 40 - cards.Sum(card => card.Copies);
            for (int row = 0; left > 0; row++) {
                int count = Math.Min(distinct ? 1 : 3, left);
                cards.Add(new([], count, "Filler " + row, id: "filler-" + row));
                left -= count;
            }
            List<Combo> combos = [
                new([], "First target", groupId: "access", cards: [new(selected[0].Id, 1, 5)]),
                new([new(other, 1, 5)], "Target plus other", groupId: "access", cards: [new(selected[^1].Id, 1, 5)]),
                new([], "No source or first target", cards: [new(source.Id, 0, 0), new(selected[0].Id, 0, 0)])
            ];
            cases.Add((name, new() { Cards = cards, Combos = combos, HandSize = hand,
                ComboGroups = [new("access", "Access union")] }));
        }
        // A fetched target is eligible for Tract's post-search removal, including
        // when a zero maximum makes fetching and then discarding it useful.
        SessionState tract = cases.Single(value => value.Name == "tract-many").State;
        tract.Combos.Add(new([], "No searched targets", cards: tract.Cards.Where(card => card.Id.StartsWith("target-"))
            .Select(card => new ComboCard(card.Id, 0, 0))));
        SessionState mixed = new() {
            Cards = [.. cases.Single(value => value.Name == "branded-one").State.Cards],
            Combos = cases[0].State.Combos, ComboGroups = cases[0].State.ComboGroups, HandSize = 5
        };
        int filler = mixed.Cards.FindIndex(card => card.Id == "filler-0");
        mixed.Cards[filler] = new([], 3, "Filtered draw with hidden target loss", id: "draw",
            drawCount: 1, postDrawDiscardCount: 1,
            postDrawSelector: CardSelector.For(mixed.Cards.Single(card => card.Id == "other-a").Categories[0]),
            postDrawDeckMillCount: 2);
        cases.Add(("mixed-filtered-hidden", mixed));
        SessionState groupedMixed = new() {
            Cards = [.. mixed.Cards.Where(card => !card.Id.StartsWith("filler-")),
                new([], mixed.Cards.Where(card => card.Id.StartsWith("filler-")).Sum(card => card.Copies), id: "filler")],
            Combos = mixed.Combos, ComboGroups = mixed.ComboGroups, HandSize = 5
        };
        cases.Add(("mixed-grouped-hidden", groupedMixed));
        CategoryBase referenceTarget = new("Target");
        CategoryBase referenceOther = new("Other");
        CategoryBase referenceMaterial = new("Material");
        cases.Add(("ordered-219-completed", new() {
            Cards = [new([], 2, id: "s", search: new(CardSelector.For(referenceTarget), false)),
                new([referenceTarget], 3, id: "t"), new([referenceOther], 3, id: "b"), new([referenceMaterial], 2, id: "m"),
                new([], 28, id: "f"), new([], 1, id: "d", drawCount: 2, postDrawDiscardCount: 1,
                    postDrawSelector: CardSelector.For(referenceTarget), postDrawDeckMillCount: 2),
                new([], 1, id: "e", drawCount: 1, firstActivationOnly: true)],
            HandSize = 5, Combos = [new([new(referenceTarget, 1, 2)], "One or two targets", groupId: "g"),
                new([new(referenceTarget, 1, 5), new(referenceOther, 1, 5)], "Distinct target and other", groupId: "g")],
            ComboGroups = [new("g", "Routes")]
        }));
        SessionState ordinary = await SearchAvailabilityCertificateTest.Example("plain");
        for (int row = 0; row < ordinary.Cards.Count; row++) {
            ordinary.Cards[row] = ordinary.Cards[row].WithSearch(null);
        }
        cases.Add(("no-search-control", ordinary));
        SessionState ordered = await SearchAvailabilityCertificateTest.Example("mill2");
        for (int row = 0; row < ordered.Cards.Count; row++) {
            if (ordered.Cards[row].DrawCount is not null) {
                ordered.Cards[row] = ordered.Cards[row].WithPostDrawDiscardCount(1);
            }
        }
        cases.Add(("ordered-219-reference", ordered));
        cases.Add(("ordered-219-eleven", OrderedTerminalAggregationTest.Pressure("eleven", roles: 11)));
        SessionService codec = new(null!, new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());
        List<object> output = [];
        foreach ((string name, SessionState state) in cases) {
            Assert.That(state.Cards.Sum(card => card.Copies), Is.EqualTo(40), name);
            foreach (CalculationModel mode in new[] { CalculationModel.Fast, CalculationModel.Exact }) {
                output.Add(new { Name = name + "-" + mode, Input = JsonSerializer.Deserialize<JsonElement>(
                    CalculationSnapshot.Capture(state.Cards, state.Combos, state.HandSize, state.ComboGroups,
                        CalculationWorkPolicy.Interactive, storagePolicy: CalculationStoragePolicy.Standard, options: new(Mode: mode)).Json),
                    Session = JsonSerializer.Deserialize<JsonElement>(codec.SerializeSession(state)) });
            }
        }
        File.WriteAllText(Environment.GetEnvironmentVariable("YGO_BOUNDED_FIXTURES")
            ?? throw new InvalidOperationException("Set YGO_BOUNDED_FIXTURES."), JsonSerializer.Serialize(output));
    }
}
