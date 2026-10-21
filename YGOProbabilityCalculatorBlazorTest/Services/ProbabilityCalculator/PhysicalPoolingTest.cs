using System.Numerics;
using System.Reflection;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture, NonParallelizable]
public sealed class PhysicalPoolingTest {
    private sealed record OracleRows(BigInteger Outcomes, BigInteger[] Wins);
    private sealed record PredicateCombo(Combo Model, Func<IReadOnlyList<Card>, bool> Matches);
    private sealed record ModeRule(int Limit, Func<Card, bool> Matches);
    private sealed record SearchRule(string SourceId, int Limit, ModeRule[] Modes);

    [Test]
    public void AdjacentOrdinaryClassesMatchIndependentLabeledOrdersAcrossSeededPolicies() {
        CategoryBase a = new("A");
        CategoryBase b = new("B");
        List<ComboGroup> groups = [new("positive", "Positive"), new("zero", "Zero"), new("empty", "Empty")];
        // The seed generates inputs only; the oracle exhausts their physical outcomes.
        Random random = new(20261021);
        for (int variant = 0; variant < 24; variant++) {
            CardSelector selector = (variant % 3) switch {
                0 => CardSelector.Any,
                1 => CardSelector.For(a),
                _ => CardSelector.AnyOfSelectors([CardSelector.For(a), CardSelector.AllOf([a.Identity, b.Identity])])
            };
            bool cost = variant % 4 == 0;
            List<Card> cards = [
                new([], 1, "same name", id: "search", search: new(CardSelector.For(a), false), oncePerTurn: variant % 2 == 0),
                new([a], 1, "same name", id: "aa"),
                new([a], 2, "same name", id: "az"),
                new(random.Next(2) == 0 ? [b] : [a, b], 1, id: "middle"),
                new(
                    [],
                    1,
                    id: "draw",
                    drawCount: 1,
                    preDrawRemovalCount: cost ? 1 : 0,
                    preDrawSelector: cost ? selector : null,
                    postDrawDiscardCount: cost ? 0 : 1,
                    postDrawSelector: cost ? null : selector,
                    postDrawDeckMillCount: variant % 3 == 2 ? 1 : 0
                )
            ];
            cards.Add(
                variant % 2 == 0
                    ? new([], 1, id: "second", search: new(CardSelector.AnyOf([a.Identity, b.Identity]), false), oncePerTurn: true)
                    : new(
                        [],
                        1,
                        id: "second",
                        drawCount: 1,
                        firstActivationOnly: variant % 4 == 1,
                        blocksLaterDraws: variant % 4 == 3
                    )
            );
            List<Combo> combos = [
                new([new(a, 1, 2)], "One or two A", groupId: "positive"),
                new([new(a, 1, 3), new(b, 1, 3)], "Distinct A and B", groupId: "positive"),
                new([ComboCategory.SameCard([a, b], 1, 1)], "Same-card overlap", groupId: "positive"),
                new([new(a, 0, 0)], "No A", groupId: "zero"),
                new([], "Direct zero", groupId: "zero", cards: [new("middle", 0, 0)]),
                new(
                    [],
                    "Alternative",
                    groupId: "positive",
                    alternativeGroups: [
                        new(
                            [ComboAlternative.For(new ComboCategory(a, 1, 2)), ComboAlternative.For(new ComboCard("second", 1, 1))]
                        )
                    ]
                ),
                new([], "Completed branch")
            ];
            int handSize = 1 + variant % 3;
            string[]? priority = variant % 2 == 0 ? ["draw", "second"] : ["second", "draw"];
            SearchCertificateOracle.Counts oracle = SearchCertificateOracle.Enumerate(
                cards,
                handSize,
                combos,
                groups,
                priority,
                collapseIdenticalCopies: variant >= 2
            );
            Assert.That(
                oracle.Wins[1][1 + combos.Count - 1] + oracle.Failed,
                Is.EqualTo(oracle.Outcomes),
                $"seed 20261021/{variant}: failure mass remains in the denominator"
            );
            foreach (PhysicalEnumerationMethod method in Methods) {
                ExactCalculationResult unmerged = Calculate(cards, handSize, combos, groups, false, method, priority, out _);
                ExactCalculationResult pooled = Calculate(cards, handSize, combos, groups, true, method, priority, out int rows);
                Assert.That(rows, Is.LessThan(cards.Count), $"seed 20261021/{variant}: candidate actually pools");
                AssertRows(unmerged, oracle.Wins[1], oracle.Outcomes, $"reference/{variant}/{method}");
                AssertRows(pooled, oracle.Wins[1], oracle.Outcomes, $"pooled/{variant}/{method}");
            }
        }
    }

    [Test]
    public void FiveCardOpenerPreservesPooledPhysicalMassAcrossAllRoutes() {
        CategoryBase a = new("A");
        CategoryBase b = new("B");
        List<Card> cards = [
            new([], id: "search", search: new(CardSelector.For(a), false)),
            new([a], id: "aa"),
            new([a], 2, id: "az"),
            new([b], id: "middle"),
            new(
                [],
                id: "draw",
                drawCount: 1,
                preDrawDeckRemovalCount: 1,
                postDrawDiscardCount: 1,
                postDrawSelector: CardSelector.Any
            ),
            new([], id: "filler")
        ];
        List<Combo> combos = [
            new([new(a, 1, 2)], "One or two A", groupId: "positive"),
            new([new(a, 1, 3), new(b, 1, 1)], "Distinct A and B", groupId: "positive"),
            new([new(a, 0, 0)], "No A", groupId: "zero"),
            new([], "No middle", groupId: "zero", cards: [new("middle", 0, 0)]),
            new([], "Completed branch")
        ];
        List<ComboGroup> groups = [new("positive", "Positive"), new("zero", "Zero"), new("empty", "Empty")];
        SearchCertificateOracle.Counts oracle = SearchCertificateOracle.Enumerate(
            cards,
            5,
            combos,
            groups
        );
        Assert.That(oracle.Wins[1][combos.Count] + oracle.Failed, Is.EqualTo(oracle.Outcomes));
        foreach (PhysicalEnumerationMethod method in Methods) {
            foreach (bool pool in new[] { false, true }) {
                ExactCalculationResult result = Calculate(cards, 5, combos, groups, pool, method, null, out int rows);
                Assert.That(rows, Is.EqualTo(pool ? 5 : 6), $"five-card opener/{method}/{pool}");
                AssertRows(result, oracle.Wins[1], oracle.Outcomes, $"five-card opener/{method}/{pool}");
            }
        }
    }

    [Test]
    public void NumericComboTruthSeparatesLowAndHighCardsBeforePooling() {
        CategoryBase monster = new("Monster", CategorySource.Metadata, "kind:monster");
        CategoryBase low = new("Level 4", CategorySource.Metadata, "level:4");
        CategoryBase high = new("Level 8", CategorySource.Metadata, "level:8");
        CategoryBase range = new NumericPropertyRange(NumericPropertyKind.Level, 5, null).ToCategory();
        List<Card> cards = [
            new([], 1, id: "source", search: new(CardSelector.Any, false), activationLimit: 1),
            new([monster, low], 1, id: "a-low"),
            new([monster, high], 1, id: "b-high"),
            new([monster, high], 1, id: "c-high"),
            new([], 1, id: "z-filler")
        ];
        bool IsHigh(Card card) => card.Id is "b-high" or "c-high";
        List<PredicateCombo> definitions = [
            new(new([new(range, 1, 1)], "Exactly one high", groupId: "g"), hand => hand.Count(IsHigh) == 1),
            new(new([new(range, 0, 0)], "No high", groupId: "g"), hand => hand.All(card => !IsHigh(card)))
        ];
        List<ComboGroup> groups = [new("g", "Either"), new("empty", "Empty")];
        OracleRows oracle = EnumerateSimplePhysicalSearch(
            cards,
            2,
            definitions,
            groups,
            [new("source", 1, [new(1, _ => true)])]
        );
        List<Combo> combos = definitions.Select(definition => definition.Model).ToList();
        foreach (bool pool in new[] { false, true }) {
            ExactCalculationResult result = Calculate(cards, 2, combos, groups, pool, PhysicalEnumerationMethod.Streaming, null, out int rows);
            Assert.That(rows, Is.EqualTo(pool ? 4 : 5));
            AssertRows(result, oracle.Wins, oracle.Outcomes, "numeric selector/" + pool);
        }
    }

    [Test]
    public void ObjectiveSearchEligibilitySeparatesImportedAndManualLookalikes() {
        CategoryBase target = new("Target metadata", CategorySource.Metadata, "audit:target");
        List<Card> cards = [
            new([], 1, id: "source", search: new(CardSelector.For(target), true)),
            new([target], 1, id: "objective", externalCardId: 1001),
            new([target], 1, id: "manual-a", externalCardId: 1002, manualMetadataCategoryKeys: ["audit:target"]),
            new([target], 1, id: "manual-b", externalCardId: 1003, manualMetadataCategoryKeys: ["audit:target"]),
            new([], 1, id: "filler")
        ];
        bool IsTarget(Card card) => card.Id is "objective" or "manual-a" or "manual-b";
        List<PredicateCombo> definitions = [
            new(new([new(target, 2, 2)], "Two target roles", groupId: "g"), hand => hand.Count(IsTarget) == 2),
            new(new([new(target, 0, 0)], "No target role", groupId: "g"), hand => hand.All(card => !IsTarget(card)))
        ];
        List<ComboGroup> groups = [new("g", "Either"), new("empty", "Empty")];
        // Manual additions satisfy the combo role, but only objective can be
        // fetched. In particular source+objective cannot fetch a second target.
        OracleRows oracle = EnumerateSimplePhysicalSearch(
            cards,
            2,
            definitions,
            groups,
            [new("source", 1, [new(1, card => card.Id == "objective")])]
        );
        List<Combo> combos = definitions.Select(definition => definition.Model).ToList();
        foreach (bool pool in new[] { false, true }) {
            ExactCalculationResult result = Calculate(cards, 2, combos, groups, pool, PhysicalEnumerationMethod.Streaming, null, out int rows);
            Assert.That(rows, Is.EqualTo(pool ? 4 : 5));
            AssertRows(result, oracle.Wins, oracle.Outcomes, "objective versus manual/" + pool);
        }
    }

    [Test]
    public void SearchModesKeepTheirSeparateTargetIncidenceAndSharedEntryAllowance() {
        CategoryBase a = new("Mode A");
        CategoryBase b = new("Mode B");
        CategoryBase target = new("Target");
        CardSelector aSelector = CardSelector.For(a);
        CardSelector bSelector = CardSelector.For(b);
        List<Card> cards = [
            new(
                [],
                2,
                id: "source",
                search: new(
                    CardSelector.AnyOfSelectors([aSelector, bSelector]),
                    false,
                    [new(aSelector, 1), new(bSelector, 1)]
                ),
                activationLimit: 2
            ),
            new([a, target], 1, id: "a"),
            new([a, target], 1, id: "aa"),
            new([b, target], 1, id: "b")
        ];
        List<PredicateCombo> definitions = [
            new(new([new(target, 2, 2)], "Two targets", groupId: "g"), hand => hand.All(card => card.Id != "source")),
            new(new([new(target, 0, 0)], "No targets", groupId: "g"), hand => hand.All(card => card.Id == "source"))
        ];
        List<ComboGroup> groups = [new("g", "Either"), new("empty", "Empty")];
        OracleRows oracle = EnumerateSimplePhysicalSearch(
            cards,
            2,
            definitions,
            groups,
            [new("source", 2, [new(1, card => card.Id is "a" or "aa"), new(1, card => card.Id == "b")])]
        );
        List<Combo> combos = definitions.Select(definition => definition.Model).ToList();
        foreach (bool pool in new[] { false, true }) {
            ExactCalculationResult result = Calculate(cards, 2, combos, groups, pool, PhysicalEnumerationMethod.Streaming, null, out int rows);
            Assert.That(rows, Is.EqualTo(pool ? 3 : 4));
            AssertRows(result, oracle.Wins, oracle.Outcomes, "mode incidence/" + pool);
        }
    }

    [Test]
    public void NonAdjacentEquivalentTargetsCannotCrossAPaymentTieBoundary() {
        CategoryBase material = new("Material");
        CategoryBase payment = new("Payment");
        List<Card> cards = [
            new([material], 1, id: "a"),
            new([material, payment], 1, id: "m"),
            new([material], 1, id: "z"),
            new([], 1, id: "draw", drawCount: 1, postDrawDiscardCount: 1, postDrawSelector: CardSelector.For(material)),
            new([], 1, id: "pay", drawCount: 1, preDrawRemovalCount: 1, preDrawSelector: CardSelector.For(payment)),
            new([], 1, id: "search", search: new(CardSelector.For(material), false)),
            new([], 2, id: "filler")
        ];
        List<Combo> combos = [
            new([], "Payment source consumed", groupId: "g", cards: [new("pay", 0, 0)]),
            new([new(material, 0, 0)], "No material", groupId: "g")
        ];
        List<ComboGroup> groups = [new("g", "Either"), new("empty", "Empty")];
        string[] priority = ["draw", "pay"];
        // This order exposes the boundary: discard z, retain m, then pay m.
        // Pooling a/z at a would instead discard m and leave pay unactivated.
        Card Find(string id) => cards.Single(card => card.Id == id);
        Card[] witness = [Find("draw"), Find("pay"), Find("m"), Find("z"), Find("filler"), Find("filler"), Find("a"), Find("search")];
        List<Card>? final = DrawSequenceOracle.Resolve(witness, 4, combos, priority);
        Assert.That(final, Is.Not.Null);
        Assert.That(final!.Any(card => card.Id == "pay"), Is.False);
        SearchCertificateOracle.Counts oracle = SearchCertificateOracle.Enumerate(cards, 4, combos, groups, priority);
        foreach (PhysicalEnumerationMethod method in Methods) {
            ExactCalculationResult result = Calculate(cards, 4, combos, groups, true, method, priority, out int rows);
            Assert.That(rows, Is.EqualTo(cards.Count), "The intervening payment class prevents an a/z merge.");
            AssertRows(result, oracle.Wins[1], oracle.Outcomes, "non-adjacent tie/" + method);
        }
    }

    private static PhysicalEnumerationMethod[] Methods =>
        [PhysicalEnumerationMethod.Streaming, PhysicalEnumerationMethod.BatchStreaming, PhysicalEnumerationMethod.Frontier];

    [Test]
    public void PooledMetadataCopiesHonorWorkAndRetainedStorageBoundaries() {
        CategoryBase[] first = Enumerable.Range(0, 128)
            .Select(index => new CategoryBase("First " + index, CategorySource.Metadata, "first:" + index)).ToArray();
        CategoryBase[] second = Enumerable.Range(0, 96)
            .Select(index => new CategoryBase("Second " + index, CategorySource.Metadata, "second:" + index)).ToArray();
        List<Card> cards = [
            new(first, 1, id: "a-rich", manualMetadataCategoryKeys: [first[^1].MetadataKey!, first[^2].MetadataKey!]),
            new([], 1, id: "b-empty"),
            new([], 1, id: "m-source", search: new(CardSelector.Any, false), activationLimit: 1),
            new(second, 1, id: "y-rich", manualMetadataCategoryKeys: [second[^1].MetadataKey!]),
            new([], 1, id: "z-empty")
        ];
        List<Combo> combos = [new([], "Empty hand succeeds")];
        WorkBudget measured = new(CalculationWorkPolicy.Unlimited);
        ExactCalculationResult expected = Run(measured, out PostDrawCalculation execution);
        Assert.That(expected.Total.ToFraction(), Is.EqualTo(new ProbabilityFraction("1", "1")));
        Assert.That(execution.ReducedRows, Is.EqualTo(3));
        Assert.That(execution.LargestStructure, Is.EqualTo("physical row partition"));
        Assert.That(
            measured.PeakCells,
            Is.GreaterThan(2L * (first.Length + second.Length)),
            "Both pooled representatives own category arrays alongside the partition vectors."
        );
        CalculationStoragePolicy sufficient = Storage(measured.PeakCells);
        ExactCalculationResult actual = Run(new(new(measured.Spent), sufficient), out _);
        Assert.That(actual.Total, Is.EqualTo(expected.Total));
        ProbabilityCalculationLimitException work = Assert.Throws<ProbabilityCalculationLimitException>(
            () => Run(new(new(measured.Spent - 1), sufficient), out _)
        )!;
        Assert.That(work.Reason, Is.EqualTo(ProbabilityCalculationLimitReason.Work));
        PostDrawCalculation rejected = null!;
        ProbabilityCalculationLimitException storage = Assert.Throws<ProbabilityCalculationLimitException>(
            () => Run(new(CalculationWorkPolicy.Unlimited, Storage(measured.PeakCells - 1)), out rejected)
        )!;
        Assert.That(storage.Reason, Is.EqualTo(ProbabilityCalculationLimitReason.Storage));
        Assert.That(rejected.LargestStructure, Is.EqualTo("physical row partition"));

        ExactCalculationResult Run(WorkBudget budget, out PostDrawCalculation calculation) {
            calculation = new(cards, 0, budget, deferIdentities: false);
            return calculation.CalculateSearch(
                combos,
                [],
                false,
                new(cards, budget),
                physical: true,
                method: PhysicalEnumerationMethod.Streaming
            );
        }

        static CalculationStoragePolicy Storage(long cells) {
            // Unsupported tiny limits remain local to this resource-boundary test.
            ConstructorInfo constructor = typeof(CalculationStoragePolicy).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                [typeof(CalculationStorageTier), typeof(long), typeof(long)],
                null
            )!;
            return (CalculationStoragePolicy)constructor.Invoke([CalculationStorageTier.Standard, CalculationStoragePolicy.Standard.EntryLimit, cells]);
        }
    }

    private static ExactCalculationResult Calculate(
        List<Card> cards,
        int handSize,
        List<Combo> combos,
        List<ComboGroup> groups,
        bool pool,
        PhysicalEnumerationMethod method,
        string[]? priority,
        out int rows
    ) {
        WorkBudget budget = new(CalculationWorkPolicy.Unlimited);
        PostDrawCalculation calculation = new(
            cards,
            handSize,
            budget,
            priority is null ? null : new(DrawActivationMode.CustomPriority, priority),
            deferIdentities: false
        );
        ExactCalculationResult result = calculation.CalculateSearch(
            combos,
            groups,
            false,
            new(cards, budget),
            physical: true,
            method: method,
            coalesceOrdinary: pool
        );
        rows = calculation.ReducedRows;
        return result;
    }

    private static void AssertRows(ExactCalculationResult result, BigInteger[] wins, BigInteger outcomes, string label) {
        ExactProbability[] rows = [result.Total, .. result.Combos, .. result.Groups];
        Assert.That(rows.Length, Is.EqualTo(wins.Length), label);
        for (int row = 0; row < rows.Length; row++) {
            Assert.That(rows[row].Numerator * outcomes, Is.EqualTo(wins[row] * rows[row].Denominator), label + "/" + row);
        }
    }

    // Enumerate labeled opening subsets and optional per-original-copy search
    // choices. Rules and final predicates are supplied independently of model
    // selectors/compilers. These fixtures have no hand costs or draw phase.
    private static OracleRows EnumerateSimplePhysicalSearch(
        List<Card> cards,
        int handSize,
        List<PredicateCombo> combos,
        List<ComboGroup> groups,
        SearchRule[] rules
    ) {
        Card[] labels = cards.SelectMany(card => Enumerable.Repeat(card, card.Copies)).ToArray();
        int[] selected = new int[handSize];
        BigInteger outcomes = BigInteger.Zero;
        BigInteger[] wins = new BigInteger[1 + combos.Count + groups.Count];
        Choose(0, 0);
        return new(outcomes, wins);

        void Choose(int start, int position) {
            if (position == handSize) {
                CountHand();
                return;
            }
            for (int label = start; label <= labels.Length - handSize + position; label++) {
                selected[position] = label;
                Choose(label + 1, position + 1);
            }
        }

        void CountHand() {
            outcomes++;
            Card[] original = selected.Select(index => labels[index]).ToArray();
            Card[] hand = (Card[])original.Clone();
            bool[] remaining = Enumerable.Repeat(true, labels.Length).ToArray();
            foreach (int index in selected) {
                remaining[index] = false;
            }
            int[] activations = new int[rules.Length];
            int[][] modes = rules.Select(rule => new int[rule.Modes.Length]).ToArray();
            bool[] success = new bool[combos.Count];
            Visit(0);
            if (success.Any(value => value)) {
                wins[0]++;
            }
            for (int combo = 0; combo < combos.Count; combo++) {
                if (success[combo]) {
                    wins[1 + combo]++;
                }
            }
            for (int group = 0; group < groups.Count; group++) {
                if (combos.Where((definition, index) => success[index] && definition.Model.GroupId == groups[group].Id).Any()) {
                    wins[1 + combos.Count + group]++;
                }
            }

            void Visit(int slot) {
                if (slot == handSize) {
                    for (int combo = 0; combo < combos.Count; combo++) {
                        success[combo] |= combos[combo].Matches(hand);
                    }
                    return;
                }
                Visit(slot + 1);
                int rule = Array.FindIndex(rules, value => value.SourceId == original[slot].Id);
                if (rule < 0 || activations[rule] >= rules[rule].Limit) {
                    return;
                }
                for (int mode = 0; mode < rules[rule].Modes.Length; mode++) {
                    ModeRule definition = rules[rule].Modes[mode];
                    if (modes[rule][mode] >= definition.Limit) {
                        continue;
                    }
                    for (int target = 0; target < labels.Length; target++) {
                        if (!remaining[target] || labels[target].Id == original[slot].Id || !definition.Matches(labels[target])) {
                            continue;
                        }
                        remaining[target] = false;
                        hand[slot] = labels[target];
                        activations[rule]++;
                        modes[rule][mode]++;
                        Visit(slot + 1);
                        modes[rule][mode]--;
                        activations[rule]--;
                        hand[slot] = original[slot];
                        remaining[target] = true;
                    }
                }
            }
        }
    }
}
