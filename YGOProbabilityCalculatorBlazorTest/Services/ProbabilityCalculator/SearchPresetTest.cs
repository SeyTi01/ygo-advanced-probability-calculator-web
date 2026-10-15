using System.Diagnostics;
using System.Numerics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.CardEffects;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

public sealed partial class SearchEffectsTest {
    internal static Card Preset(int identity, int copies = 1, string? id = null) => CardEffectRegistry.Default.RestoreBuiltIn(
        new([Spell], copies, id: id ?? $"source-{identity}", externalCardId: identity));

    private static Card Element(int level = 5, string attribute = "dark", string race = "aqua", string id = "element") => new([
        A, B, new("Monster", CategorySource.Metadata, "kind:monster"), new("K9", CategorySource.Metadata, "archetype:k9"),
        new("Attribute", CategorySource.Metadata, $"attribute:{attribute}"),
        new("Type", CategorySource.Metadata, $"monster-race:{race}"), new("Level", CategorySource.Metadata, $"level:{level}")
    ], id: id, externalCardId: 100);

    [TestCase("light", "pyro", true), TestCase("light", "aqua", true)]
    [TestCase("dark", "pyro", true), TestCase("dark", "aqua", true)]
    [TestCase("light", "warrior", false), TestCase("dark", "dragon", false), TestCase("dark", "machine", false)]
    [TestCase("earth", "pyro", false), TestCase("earth", "aqua", false), TestCase("earth", "warrior", false)]
    public void ChaoticConjoinsAttributeAndRaceGroups(string attribute, string race, bool eligible) {
        SearchEffectPlan search = Preset(92221402).Search!;
        Assert.That(search.Matches(Element(attribute: attribute, race: race)), Is.EqualTo(eligible));
        Assert.That(search.Matches(Element(13, attribute, race)), Is.EqualTo(eligible));
        Assert.That(search.Matches(Element(4, attribute, race)), Is.False);
    }

    [Test]
    public void PresetsRejectUnknownRankSpellAndCounterfeitMetadataAndDescribeScope() {
        Card target = Element();
        Card rank = target.WithObjectiveMetadata(target.Categories.Where(category => category.Source == CategorySource.Metadata && category.MetadataKey != "level:5")
            .Append(new("Rank 5", CategorySource.Metadata, "rank:5")), 100);
        Card fakeSpell = new Card([Spell], externalCardId: 101);
        foreach (CategoryBase property in target.Categories.Where(category => category.Source == CategorySource.Metadata)) {
            fakeSpell = fakeSpell.WithManualMetadataCategory(property);
        }
        foreach (int identity in new[] { 80181649, 92221402 }) {
            Card source = Preset(identity);
            SearchEffectPlan search = source.Search!;
            Assert.Multiple(() => {
                Assert.That(source.OncePerTurn, Is.True);
                Assert.That(source.DrawOncePerTurn, Is.True, "The legacy API aliases the shared flag.");
                Assert.That(search.Matches(target), Is.True);
                Assert.That(search.Matches(target.WithCopies(0)), Is.False);
                Assert.That(search.Matches(target.WithActive(false)), Is.False);
                Assert.That(search.Matches(new(target.Categories)), Is.False);
                Assert.That(search.Matches(new([], externalCardId: 100)), Is.False);
                Assert.That(search.Matches(fakeSpell), Is.False);
            });
        }
        Assert.That(Preset(92221402).Search!.Matches(rank), Is.False);
        Card spellK9 = new([Spell, new("K9", CategorySource.Metadata, "archetype:k9")], externalCardId: 100);
        Assert.That(Preset(80181649).Search!.Matches(spellK9), Is.False);
        EffectiveCardEffect chaotic = CardEffectRegistry.Default.Describe(Preset(92221402));
        Assert.That(chaotic.Support, Is.EqualTo(EffectSupport.Partial));
        Assert.That(chaotic.Limitations, Does.Contain("Main Deck").And.Contain("GY retrieval").And.Contain("destruction"));
    }

    [TestCase(1), TestCase(2)]
    public void TwoHeldOptCopiesCannotSearchTwiceButRotaCan(int targetCopies) {
        Card target = Element().WithCopies(targetCopies);
        Card source = Preset(80181649, 2);
        List<Combo> combos = AssignmentObjectives(target.Id);
        Compare([source, target, new([B], id: "b"), new([], id: "blank")], combos, 2);
        WorkBudget budget = new(CalculationWorkPolicy.Interactive);
        ExactCalculationResult held = new SearchProbabilityCalculation([], 0, [source],
            new([source, target], budget), 2, budget).Calculate([new([], cards: [new(target.Id, 2, 2)])], [], false);
        AssertRatio(held.Total, 0, 1);
        Card rota = Rota(2);
        Card warrior = Warrior(2);
        budget = new(CalculationWorkPolicy.Interactive);
        held = new SearchProbabilityCalculation([], 0, [rota], new([rota, warrior], budget), 2, budget)
            .Calculate([new([], cards: [new(warrior.Id, 2, 2)])], [], false);
        AssertRatio(held.Total, 1, 1);
        Assert.That(rota.OncePerTurn, Is.False);
    }

    [TestCase(false), TestCase(true)]
    public void SplitEntriesHaveIndependentAllowancesRegardlessOfArtworkOrDisplayName(bool knownIdentity) {
        Card first = Preset(80181649, id: "first");
        Card second = first.Duplicate().WithName("Other artwork").WithObjectiveMetadata([Spell], 999);
        if (!knownIdentity) {
            first = new Card([Spell], id: first.Id).WithEffect(first.EffectPlan, EffectProfile.Legacy);
            second = new Card([Spell], id: second.Id).WithEffect(second.EffectPlan, EffectProfile.Legacy);
        }
        Card target = Element().WithCopies(2);
        List<Card> deck = [first, second, target, new([], id: "blank")];
        Compare(deck, AssignmentObjectives(target.Id), 2);
        WorkBudget budget = new(CalculationWorkPolicy.Interactive);
        ExactCalculationResult held = new SearchProbabilityCalculation([], 0, [first, second], new(deck, budget), 2, budget)
            .Calculate([new([], cards: [new(target.Id, 2, 2)])], [], false);
        AssertRatio(held.Total, 1, 1);
        deck.Reverse();
        Compare(deck, AssignmentObjectives(target.Id), 2);
    }

    [Test]
    public void DifferentlyNamedOptSourcesWithSameSelectorHaveSeparateAllowances() {
        Card first = Preset(80181649);
        Card second = Preset(92221402).WithSearch(first.Search);
        Card target = Element().WithCopies(2);
        Compare([first, second, target, new([], id: "blank")], AssignmentObjectives(target.Id), 2);
        WorkBudget budget = new(CalculationWorkPolicy.Interactive);
        ExactCalculationResult held = new SearchProbabilityCalculation([], 0, [first, second], new([first, second, target], budget), 2, budget)
            .Calculate([new([], cards: [new(target.Id, 2, 2)])], [], false);
        AssertRatio(held.Total, 1, 1);
    }

    [Test]
    public void CombinedRotaCaseChaoticCompeteForPhysicalTargetsAndOptionalAssignments() {
        Card target = Element();
        Card warrior = Warrior().WithCategories([.. Warrior().Categories, new("K9", CategorySource.Metadata, "archetype:k9")]);
        List<Card> cards = [Rota(), Preset(80181649, 2), Preset(92221402), warrior, target, new([], id: "blank")];
        foreach (int handSize in new[] { 1, 2, 3 }) {
            Compare(cards, AssignmentObjectives(target.Id), handSize);
        }
        // Draw/removal still finishes before OPT search assignment, with no new
        // draw identity dimension. All paths use the labeled-copy reference.
        cards[1] = cards[1].WithCopies(1);
        cards.Add(new([], id: "draw", drawCount: 1, postDrawDiscardCount: 1));
        Compare(cards, AssignmentObjectives(target.Id), 2);
    }

    private static List<Combo> AssignmentObjectives(string target) => [
        new([], "direct", groupId: "g1", cards: [new(target, 1, 5)]),
        new([], "two targets", groupId: "g1", cards: [new(target, 2, 5)]),
        new([new(Spell, 1, 5)], "optional source", groupId: "g2"),
        new([new(Spell, 1, 5)], "source consumption", groupId: "g2", cards: [new(target, 1, 5)]),
        new([new(A, 0, 0), new(B, 1, 5)], "exclude", groupId: "g1"),
        new([new(A, 1, 1), new(B, 1, 5)], "distinct maximum", groupId: "g2"),
        new([ComboCategory.SameCard([A, B], 1, 5)], "AND"),
        new([], "OR", alternativeGroups: [new([ComboAlternative.For(new ComboCard(target, 1, 5)), ComboAlternative.For(new ComboCategory(Spell, 1, 5))])])
    ];

    [Test]
    public async Task UpdatedExampleKeepsOnlyGenuineMetadataAndExactTargetSets() {
        SessionState session = await LoadExample("search_example_session_state.json");
        SessionState previous = await LoadExample("rota_search_example_session_state.json");
        Assert.That(session.Cards.Count, Is.EqualTo(25));
        Assert.That(session.Cards.Sum(card => card.Copies), Is.EqualTo(40));
        Assert.That(session.Cards.Select(card => (card.Id, card.Copies)), Is.EqualTo(previous.Cards.Select(card => (card.Id, card.Copies))));
        Card caseK9 = session.Cards.Single(card => card.ExternalCardId == 80181649);
        Card chaotic = session.Cards.Single(card => card.ExternalCardId == 92221402);
        Assert.That(session.Cards.Where(caseK9.Search!.Matches).Select(card => card.Name), Is.EquivalentTo(new[] {
            "K9-04 Noroi", "K9-17 Izuna", "K9-66a Jokul", "K9-66b Lantern", "K9-ØØ Lupis"
        }));
        Assert.That(session.Cards.Where(chaotic.Search!.Matches).Select(card => card.Name),
            Is.EquivalentTo(new[] { "K9-66a Jokul", "K9-66b Lantern" }));
        foreach (Card card in new[] { caseK9, chaotic }) {
            Assert.That(card.ManualMetadataCategoryKeys, Is.Empty);
            Assert.That(card.Categories.All(category => category.Source == CategorySource.Metadata), Is.True);
            Assert.That(card.Categories.Any(category => category.MetadataKey == "kind:spell"), Is.True);
            Assert.That(card.EffectPlan, Is.EqualTo(Preset(card.ExternalCardId!.Value).EffectPlan));
        }
        foreach (Card card in session.Cards.Except([caseK9, chaotic])) {
            Card old = previous.Cards.Single(candidate => candidate.Id == card.Id);
            Assert.That(card.EffectPlan, Is.EqualTo(old.EffectPlan));
            Assert.That(card.Categories.Select(category => category.Identity), Is.EqualTo(old.Categories.Select(category => category.Identity)));
        }
        WorkBudget budget = new(CalculationWorkPolicy.Interactive, CalculationStoragePolicy.Standard);
        Stopwatch timer = Stopwatch.StartNew();
        ExactCalculationResult result = new DrawEffectCalculation(session.Cards, 5, budget).Calculate(session.Combos, session.ComboGroups);
        timer.Stop();
        long[] expected = [550033, 271496, 64470, 150480, 117819, 16446, 313972, 23346, 22091, 402929, 323132];
        ExactProbability[] results = [result.Total, .. result.Combos, .. result.Groups];
        for (int i = 0; i < results.Length; i++) {
            AssertRatio(results[i], expected[i], 658008);
        }
        long[] oracle = SearchExampleOracle.Count(session);
        Assert.That(oracle[0], Is.EqualTo(658008));
        Assert.That(oracle.Skip(1), Is.EqualTo(expected));
        TestContext.Out.WriteLine($"Updated example: {timer.Elapsed.TotalMilliseconds:F2}ms, {budget.Spent} work, {budget.PeakCells} cells; "
            + string.Join(", ", new[] { result.Total }.Concat(result.Combos).Concat(result.Groups).Select(p => $"{p.Numerator}/{p.Denominator}")));
    }
}
