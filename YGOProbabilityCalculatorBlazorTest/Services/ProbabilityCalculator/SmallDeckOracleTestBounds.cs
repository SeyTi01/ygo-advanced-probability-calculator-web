using System.Diagnostics;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public sealed class SmallDeckOracleTestBounds : SmallDeckOracleTestBase
{
    [TestCase(false)]
    [TestCase(true)]
    public void AnyAndFixedFiveDivergeOnlyAtSixCards(bool directCard)
    {
        Card starter = new([A], 6);
        List<Card> deck = [starter, new([], 2)];

        Combo Requirement(RequirementMaximumMode mode) => directCard
            ? new([], cards: [new(starter.Id, 1, 5, mode)])
            : new([new(A, 1, 5, mode)]);

        Combo fixedFive = Requirement(RequirementMaximumMode.Fixed);
        Combo any = Requirement(RequirementMaximumMode.HandSize);
        // Enumerate physical hands independently. Of 28 six-card hands, only the
        // hand containing all six starters violates fixed 5; all 56 five-card hands pass.
        Assert.That(SmallDeckOracle.EnumerateCounts(deck, [fixedFive], 5), Is.EqualTo((56, 56)));
        Assert.That(SmallDeckOracle.EnumerateCounts(deck, [any], 5), Is.EqualTo((56, 56)));
        Assert.That(SmallDeckOracle.EnumerateCounts(deck, [fixedFive], 6), Is.EqualTo((27, 28)));
        Assert.That(SmallDeckOracle.EnumerateCounts(deck, [any], 6), Is.EqualTo((28, 28)));

        foreach (int size in new[] { 5, 6 })
        {
            AssertTotalAndStandaloneResultsMatchEveryPhysicalHand(deck, [fixedFive, any], size);
        }

        Assert.That(directCard ? fixedFive.Cards[0].MaxCount : fixedFive.Categories[0].MaxCount, Is.EqualTo(5));
    }

    [Test]
    public void DynamicDuplicatesExclusionsAndDistinctSlotsMatchPhysicalHands()
    {
        Card starter = new([A, B], 3);
        List<Card> deck = [starter, new([A], 1), new([B], 1), new([], 2)];
        List<Combo> combos =
        [
            new([new(A, 1, 0, RequirementMaximumMode.HandSize), new(A, 1, 2)]),
            new([], cards: [new(starter.Id, 1, 0, RequirementMaximumMode.HandSize), new(starter.Id, 1, 2)]),
            new([new(A, 0, 0), new(B, 1, 0, RequirementMaximumMode.HandSize)]),
            new([], cards: [new(starter.Id, 0, 0)]),
            new([new(A, 1, 0, RequirementMaximumMode.HandSize), new(B, 1, 0, RequirementMaximumMode.HandSize)],
                cards: [new(starter.Id, 1, 0, RequirementMaximumMode.HandSize)]),
            new([new(A, 4, 0, RequirementMaximumMode.HandSize)]),
            new([], cards: [new(starter.Id, 4, 0, RequirementMaximumMode.HandSize)])
        ];

        foreach (int size in new[] { 1, 2, 3, 4 })
        {
            AssertTotalAndStandaloneResultsMatchEveryPhysicalHand(deck, combos, size);
        }

        Assert.That(SmallDeckOracle.MatchesHand([starter, starter, starter], combos[0]), Is.False);
        Assert.That(SmallDeckOracle.MatchesHand([starter, starter, starter], combos[1]), Is.False);
        Assert.That(SmallDeckOracle.MatchesHand([starter, starter], combos[4]), Is.False);
        Assert.That(SmallDeckOracle.MatchesHand([starter, starter, starter], combos[4]), Is.True);
        Assert.That(SmallDeckOracle.MatchesHand([starter], combos[2]), Is.False);
        Assert.That(SmallDeckOracle.MatchesHand([starter], combos[3]), Is.False);
    }

    [Test]
    public void ManualAndObjectiveFireUseTheSamePhysicalCopySemantics()
    {
        CategoryBase fire = new("Attribute: FIRE", CategorySource.Metadata, "attribute:fire");
        CategoryBase spell = new("Spell", CategorySource.Metadata, "kind:spell");
        Card objective = new([fire], 2);
        Card rota = new Card([spell], 2).WithManualMetadataCategory(fire);
        List<Card> deck = [objective, rota, new([], 2)];
        List<Combo> combos =
        [
            new([new(fire, 1, 3)]), new([new(fire, 1, 3), new(spell, 1, 3)]),
            new([new(fire, 1, 1)]), new([new(fire, 0, 0)]), new([new(fire, 1, 3)], cards: [new(rota.Id, 1, 3)])
        ];

        foreach (int size in new[] { 1, 2, 3 })
        {
            AssertTotalAndStandaloneResultsMatchEveryPhysicalHand(deck, combos, size);
        }

        Assert.That(SmallDeckOracle.MatchesHand([rota], combos[0]), Is.True);
        Assert.That(SmallDeckOracle.MatchesHand([objective], combos[0]), Is.True);
        Assert.That(SmallDeckOracle.MatchesHand([rota], combos[1]), Is.False);
        Assert.That(SmallDeckOracle.MatchesHand([rota, rota], combos[1]), Is.True);
        Card removed = rota.WithoutManualMetadataCategory(fire.MetadataKey!);
        Assert.That(SmallDeckOracle.MatchesHand([removed], combos[0]), Is.False);

        foreach (int size in new[] { 1, 2, 3 })
        {
            AssertTotalAndStandaloneResultsMatchEveryPhysicalHand([objective, removed, deck[2]], combos, size);
        }
    }

    [Test]
    public async Task BundledExampleUsesCurrentRolesAndMetadataOverrides()
    {
        string source = await File.ReadAllTextAsync(Path.Combine(TestContext.CurrentContext.TestDirectory,
            "Fixtures",
            "example_session_state.json"));
        SessionState session = await new YGOProbabilityCalculatorBlazor.Services.Session.SessionService(
            Moq.Mock.Of<Microsoft.JSInterop.IJSRuntime>(),
            new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer()).LoadSessionAsync(source);

        Assert.That(session.Categories.Select(c => c.Identity),
            Is.EquivalentTo(new[]
            {
                "user:VS Monster", "user:VS Starter", "user:K9 Starter"
            }));
        Assert.That(session.Combos, Has.Count.EqualTo(8));
        Assert.That(session.Combos.All(combo => combo.Active), Is.True);

        Combo fireOrDark = session.Combos.Single(combo => combo.Name == "VS Starter + (Fire OR Dark)");
        Assert.That(fireOrDark.Categories, Has.Count.EqualTo(1));
        ComboCategory starter = fireOrDark.Categories.Single();
        Assert.That(starter.BaseCategory.Identity, Is.EqualTo("user:VS Starter"));
        Assert.That(starter.MinCount, Is.EqualTo(1));
        Assert.That(starter.MaximumMode, Is.EqualTo(RequirementMaximumMode.HandSize));
        Assert.That(fireOrDark.AlternativeGroups, Has.Count.EqualTo(1));
        IReadOnlyList<ComboAlternative> alternatives = fireOrDark.AlternativeGroups.Single().Alternatives;
        Assert.That(alternatives, Has.Count.EqualTo(2));
        Assert.That(alternatives.Select(alternative => alternative.Category!.BaseCategory.Identity),
            Is.EquivalentTo(new[] { "metadata:attribute:fire", "metadata:attribute:dark" }));
        Assert.That(alternatives.All(alternative => alternative.Category!.MinCount == 1
                                                    && alternative.Category.MaximumMode ==
                                                    RequirementMaximumMode.HandSize),
            Is.True);

        Card rota = session.Cards.Single(c => c.Name == "Reinforcement of the Army");
        Assert.That(rota.Categories.Select(c => c.Identity),
            Does.Contain("user:VS Monster").And.Contain("user:VS Starter").And.Contain("metadata:attribute:fire"));
        Assert.That(rota.ManualMetadataCategoryKeys, Is.EquivalentTo(new[] { "attribute:fire" }));

        Card chaoticElements = session.Cards.Single(c => c.Name == "Chaotic Elements");
        Assert.That(chaoticElements.ManualMetadataCategoryKeys,
            Is.EquivalentTo(new[] { "attribute:dark", "attribute:earth", "level:5" }));

        Card caseForK9 = session.Cards.Single(c => c.Name == "\"A Case for K9\"");
        Assert.That(caseForK9.ManualMetadataCategoryKeys,
            Is.EquivalentTo(new[] { "attribute:dark", "attribute:earth", "level:5" }));

        Combo izunaSue = session.Combos.Single(c => c.Name == "Izuna + Sue + 2 x Lv. 5");
        ComboCategory levelFive = izunaSue.Categories.Single(c => c.BaseCategory.Identity == "metadata:level:5");
        Assert.That(levelFive.MinCount, Is.EqualTo(2));

        ProbabilityCalculationResult result = new ProbabilityCalculatorService().CalculateProbabilityResults(
            [.. session.Cards.Where(card => card.Active)],
            [.. session.Combos.Where(combo => combo.Active)],
            session.HandSize,
            session.ComboGroups);
        Assert.That(result.TotalProbability * 100, Is.EqualTo(83.61).Within(0.005));
    }

    [Test]
    public void SameLabelUserAndMetadataRequirementsRemainIndependent()
    {
        CategoryBase user = new("Spell");
        CategoryBase spell = new("Spell", CategorySource.Metadata, "kind:spell");
        CategoryBase quick = new("Quick-Play Spell", CategorySource.Metadata, "spell-type:quick-play");
        List<Card> deck = [new([user], 2), new([spell, quick], 2), new([user, spell]), new([], 2)];
        List<Combo> combos =
        [
            new([new(user, 1, 3)]), new([new(spell, 1, 3)]),
            new([new(user, 1, 3), new(spell, 1, 3)]),
            new([new(spell, 1, 3), new(quick, 1, 3)]),
            new([new(user, 0, 0), new(spell, 1, 3)]),
            new([new(spell, 0, 0), new(user, 1, 3)])
        ];

        foreach (int size in new[] { 1, 2, 3 })
        {
            AssertTotalAndStandaloneResultsMatchEveryPhysicalHand(deck, combos, size);
        }

        Assert.That(SmallDeckOracle.MatchesHand([deck[1]], combos[3]), Is.False);
        Assert.That(SmallDeckOracle.MatchesHand([deck[1], deck[1]], combos[3]), Is.True);
        Assert.That(SmallDeckOracle.MatchesHand([deck[0]], combos[1]), Is.False);
        Assert.That(SmallDeckOracle.MatchesHand([deck[1]], combos[0]), Is.False);

        // Relabeling the same metadata key must not change membership eligibility.
        CategoryBase relabeled = new("New label", CategorySource.Metadata, "kind:spell");
        Combo relabeledCombo = new([new(relabeled, 1, 3)]);
        AssertTotalAndStandaloneResultsMatchEveryPhysicalHand(deck, [relabeledCombo], 2);
        Assert.That(new ProbabilityCalculatorService().CalculateProbabilityForCombos(deck, [relabeledCombo], 2),
            Is.EqualTo(SmallDeckOracle.EnumerateProbability(deck, [combos[1]], 2)));
    }

    [Test]
    public void UserOnlySemanticsRemainIdenticalToEquivalentMaterializedProperties()
    {
        List<Card> userDeck = [new([A], 2), new([B], 2), new([A, B]), new([], 2)];
        List<Combo> userCombos = [new([new(A, 1, 2), new(B, 1, 2)]), new([new(A, 0, 0)])];
        CategoryBase propertyA = new("A", CategorySource.Metadata, "a");
        CategoryBase propertyB = new("B", CategorySource.Metadata, "b");
        List<Card> propertyDeck =
        [
            .. userDeck.Select(card => card.WithCategories(card.Categories.Select(c => c == A ? propertyA : propertyB)))
        ];
        List<Combo> propertyCombos =
        [
            .. userCombos.Select(combo => combo.WithCategories(combo.Categories.Select(c =>
                new ComboCategory(c.BaseCategory == A ? propertyA : propertyB, c.MinCount, c.MaxCount, c.MaximumMode))))
        ];
        AssertTotalAndStandaloneResultsMatchEveryPhysicalHand(userDeck, userCombos, 2);
        AssertTotalAndStandaloneResultsMatchEveryPhysicalHand(propertyDeck, propertyCombos, 2);
        Assert.That(new ProbabilityCalculatorService().CalculateProbabilityForCombos(propertyDeck, propertyCombos, 2),
            Is.EqualTo(SmallDeckOracle.EnumerateProbability(userDeck, userCombos, 2)));
    }

    [Test]
    public void DirectCardAndMixedRequirementsMatchPhysicalHandsAndGroups()
    {
        CategoryBase starter = new("Starter");
        Card first = new([starter], 2, "Starter");
        Card second = new([B], 2, "Same");
        Card third = new([B], 1, "Same");
        List<Card> deck = [first, second, third, new([], 2)];
        List<Combo> combos =
        [
            new([], "Two cards", groupId: "g", cards: [new(first.Id, 1, 2), new(second.Id, 1, 1)]),
            new([new(starter, 1, 2)], "Same physical card", groupId: "g", cards: [new(first.Id, 1, 2)]),
            new([new(B, 1, 2)], "Mixed", cards: [new(third.Id, 0, 0)]),
            new([], "Repeated range", cards: [new(first.Id, 0, 2), new(first.Id, 1, 1)]),
            new([], "Contradiction", cards: [new(first.Id, 0, 0), new(first.Id, 1, 2)])
        ];

        foreach (int handSize in new[] { 1, 2, 3 })
        {
            AssertTotalAndStandaloneResultsMatchEveryPhysicalHand(deck, combos, handSize);
            ProbabilityCalculationResult result = new ProbabilityCalculatorService().CalculateProbabilityResults(deck,
                combos,
                handSize,
                [new("g", "Grouped")]);
            Assert.That(result.GroupProbabilities![0].Probability,
                Is.EqualTo(SmallDeckOracle.EnumerateProbability(deck,
                    [.. combos.Where(combo => combo.GroupId == "g")],
                    handSize)).Within(1e-12));
        }

        List<Card> activeDeck = [.. deck.Where(card => card != first)];
        AssertTotalAndStandaloneResultsMatchEveryPhysicalHand(activeDeck, combos, 2);
        Assert.That(first.WithName("Renamed").WithCopies(3).Id, Is.EqualTo(first.Id));
        Assert.That(first.WithCategories([]).WithActive(false).Id, Is.EqualTo(first.Id));
    }

    private static IEnumerable<TestCaseData> Cases()
    {
        List<Card> deck = [new([A], 2), new([B]), new([A, B]), new([B, C]), new([], 2)];

        yield return Case("positive maximum", deck, [new([new(A, 1, 1)])], 2);
        yield return Case("zero maximum", deck, [new([new(A, 0, 0)])], 2);
        yield return Case("overlapping categories", deck, [new([new(A, 1, 2), new(B, 1, 2)])], 3);
        yield return Case("overlapping combos", deck, [new([new(A, 1, 3)]), new([new(B, 1, 3)])], 3);
        yield return Case("shared intersection with coefficient minus two",
            deck,
            [
                new([new(A, 1, 3), new(B, 1, 3)]),
                new([new(A, 1, 3), new(C, 1, 3)]),
                new([new(B, 1, 3), new(C, 1, 3)])
            ],
            3);
        yield return Case("subset combos", deck, [new([new(A, 1, 2)]), new([new(A, 1, 2), new(B, 1, 2)])], 2);
        yield return Case("disjoint ranges", deck, [new([new(A, 0, 0)]), new([new(A, 2, 2)])], 2);
        yield return Case("duplicate combos",
            deck,
            [
                new([new(A, 1, 2)], "Same name"),
                new([new(A, 1, 2)], "Same name")
            ],
            2);
        yield return Case("minimum above available copies", deck, [new([new(C, 2, 3)])], 3);
        yield return Case("unconstrained combo", deck, [new([])], 3);
        yield return Case("no combos", deck, [], 2);
        yield return Case("whole deck", deck, [new([new(A, 3, 3), new(B, 3, 3)])], 7);
        yield return Case("repeated names and constraints",
            [new([A, new("A")], 2), new([B]), new([])],
            [new([new(A, 1, 2), new(new("A"), 0, 1)])],
            2);
        yield return Case("contradictory repeated constraints",
            deck,
            [new([new(A, 0, 0), new(new("A"), 1, 2)])],
            2);
        yield return Case("empty hand with zero bounds", deck, [new([new(A, 0, 0)])], 0);
        yield return Case("empty deck and unconstrained combo", [], [new([])], 0);
    }

    [TestCase(32)]
    [TestCase(33)]
    [TestCase(65)]
    public void CategoryPositionsRemainDistinct(int categoryCount)
    {
        CategoryBase[] categories = [.. Enumerable.Range(0, categoryCount).Select(i => new CategoryBase($"C{i}"))];
        List<Card> deck = [new([categories[0]]), new([categories[^1]])];
        List<Combo> combos =
        [
            new(categories.Select((category, index) =>
                new ComboCategory(category, index == 0 ? 1 : 0, index == 0 ? 1 : 0)))
        ];
        AssertTotalAndStandaloneResultsMatchEveryPhysicalHand(deck, combos, 1);
    }

    [Test]
    public void IntersectionCanIntroduceMoreThan32Categories()
    {
        CategoryBase[] categories = [.. Enumerable.Range(0, 33).Select(i => new CategoryBase($"C{i}"))];
        List<Card> deck = [new([categories[0]]), new([categories[32]]), new([])];
        List<Combo> combos =
        [
            new(categories.Take(16).Select((category, index) =>
                new ComboCategory(category, index == 0 ? 1 : 0, index == 0 ? 1 : 0))),
            new(categories.Skip(16).Select(category => new ComboCategory(category, 0, 0)))
        ];
        AssertTotalAndStandaloneResultsMatchEveryPhysicalHand(deck, combos, 1);
    }

    [TestCase(31)]
    [TestCase(32)]
    [TestCase(33)]
    [TestCase(64)]
    public void TooManyCombosAreRejectedInsteadOfReturningAnIncorrectProbability(int comboCount)
    {
        List<Card> deck = [new([A]), new([])];
        List<Combo> combos =
            [.. Enumerable.Range(0, comboCount).Select(_ => new Combo(new[] { new ComboCategory(A, 1, 1) }))];
        // Physical enumeration (and Wolfram) gives 1/2; a false zero must not be
        // presented as a result while larger union calculations are unsupported.
        ProbabilityCalculatorService service = new();
        Assert.Throws<ArgumentOutOfRangeException>(() => service.CalculateProbabilityForCombos(deck, combos, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => service.CalculateProbabilityResults(deck, combos, 1));
    }

    [Test]
    public void FortyCardDeckWithManyMembershipPatterns()
    {
        CategoryBase[] categories = [.. Enumerable.Range(0, 5).Select(i => new CategoryBase($"C{i}"))];
        List<Card> deck =
        [
            .. Enumerable.Range(0, 20).Select(pattern =>
                new Card(categories.Where((_, index) => (pattern & (1 << index)) != 0), 2))
        ];
        List<Combo> combos = [new(categories.Select(category => new ComboCategory(category, 0, 5)))];
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        Stopwatch timer = Stopwatch.StartNew();
        double probability = new ProbabilityCalculatorService().CalculateProbabilityForCombos(deck, combos, 5);
        timer.Stop();
        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        TestContext.Out.WriteLine(
            $"40 cards / 20 patterns / hand 5: {timer.Elapsed.TotalMilliseconds:F1} ms, {allocatedBytes:N0} allocated bytes");
        Assert.That(probability, Is.EqualTo(1).Within(1e-12));
        // Generous regression budget: about 9.5 MB with equivalent states merged,
        // versus 62.7 MB when array reference equality prevents merging. No timing gate.
        Assert.That(allocatedBytes, Is.LessThan(32 * 1024 * 1024));

        // Of these 40 copies, A-only / B-only / both / neither = 12 / 8 / 8 / 12
        // for A=C0, B=C2. Wolfram: Sum[C(12,a) C(12,5-a),a=1..2]/C(40,5).
        combos =
        [
            new(categories.Select((category, index) => new ComboCategory(category,
                index == 0 ? 1 : 0,
                index == 0 ? 2 : index == 2 ? 0 : 5)))
        ];
        probability = new ProbabilityCalculatorService().CalculateProbabilityForCombos(deck, combos, 5);
        Assert.That(probability, Is.EqualTo(1705.0 / 54834.0).Within(1e-12));

        // The universal event skips DP; this constrained case protects state merging.
        combos = [new(categories.Select(category => new ComboCategory(category, 1, 4)))];
        allocated = GC.GetAllocatedBytesForCurrentThread();
        probability = new ProbabilityCalculatorService().CalculateProbabilityForCombos(deck, combos, 5);
        allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        // Independent row-count enumeration with slot assignment: 248706 / C(40,5).
        Assert.That(probability, Is.EqualTo(248706.0 / 658008.0).Within(1e-12));
        Assert.That(allocatedBytes, Is.LessThan(32 * 1024 * 1024));
    }

    [Test]
    public void RenamingReorderingAndSplittingCopiesPreserveResults()
    {
        List<Card> deck = [new([A], 2), new([A, B]), new([B], 2), new([])];
        List<Combo> combos = [new([new(A, 1, 1)]), new([new(B, 0, 0)]), new([new(A, 1, 2), new(B, 1, 1)])];
        double expected = SmallDeckOracle.EnumerateProbability(deck, combos, 2);
        ProbabilityCalculatorService service = new();
        Dictionary<string, CategoryBase> renamed = new() { ["A"] = new("Renamed A"), ["B"] = new("Renamed B") };
        List<Card> splitDeck =
        [
            .. deck.AsEnumerable().Reverse().SelectMany(card => Enumerable.Range(0, card.Copies)
                .Select(_ =>
                    new Card(card.Categories.AsEnumerable().Reverse().Select(category => renamed[category.Name]))))
        ];
        List<Combo> changedCombos =
        [
            .. combos.AsEnumerable().Reverse().Select(combo => new Combo(
                combo.Categories.AsEnumerable().Reverse().Select(constraint => new ComboCategory(
                    renamed[constraint.BaseCategory.Name],
                    constraint.MinCount,
                    constraint.MaxCount,
                    constraint.MaximumMode)),
                groupId: "g"))
        ];
        Assert.That(service.CalculateProbabilityForCombos(splitDeck, changedCombos, 2),
            Is.EqualTo(expected).Within(1e-12));
        // Adding a duplicate or a subset must not enlarge the union.
        changedCombos.Add(changedCombos[0]);
        changedCombos.Add(new Combo([new(renamed["A"], 1, 1), new(renamed["B"], 1, 1)]));
        ProbabilityCalculationResult result =
            service.CalculateProbabilityResults(splitDeck, changedCombos, 2, [new("g", "Renamed group")]);
        Assert.That(result.TotalProbability, Is.EqualTo(expected).Within(1e-12));
        Assert.That(result.GroupProbabilities![0].Probability, Is.EqualTo(expected).Within(1e-12));
        changedCombos.Add(new Combo([new(renamed["A"], 0, 0)]));
        double enlarged = service.CalculateProbabilityForCombos(splitDeck, changedCombos, 2);
        Assert.That(enlarged, Is.GreaterThanOrEqualTo(expected - 1e-12));
        Assert.That(enlarged,
            Is.EqualTo(SmallDeckOracle.EnumerateProbability(splitDeck, changedCombos, 2)).Within(1e-12));
    }

    private static TestCaseData Case(string name, List<Card> deck, List<Combo> combos, int size) =>
        new TestCaseData(deck, combos, size).SetName($"Oracle: {name}");

    [TestCaseSource(nameof(Cases))]
    public void EngineMatchesEveryPhysicalHand(List<Card> deck, List<Combo> combos, int handSize)
    {
        double expected = SmallDeckOracle.EnumerateProbability(deck, combos, handSize);
        double actual = new ProbabilityCalculatorService().CalculateProbabilityForCombos(deck, combos, handSize);
        Assert.That(actual, Is.EqualTo(expected).Within(1e-12));
    }

    [TestCaseSource(nameof(Cases))]
    public void TotalAndStandaloneResultsMatchEveryPhysicalHand(List<Card> deck, List<Combo> combos, int handSize)
    {
        AssertTotalAndStandaloneResultsMatchEveryPhysicalHand(deck, combos, handSize);
    }
}
