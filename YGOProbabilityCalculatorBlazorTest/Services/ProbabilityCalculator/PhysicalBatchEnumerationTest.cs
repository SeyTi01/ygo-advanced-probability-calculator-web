using System.Numerics;
using System.Reflection;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture, NonParallelizable]
public sealed class PhysicalBatchEnumerationTest {
    private sealed record Distribution(
        Dictionary<string, BigInteger> Physical,
        BigInteger Outcomes,
        BigInteger Failed,
        BigInteger[] NoSearchWins
    );

    [Test]
    public void SystematicAndSeededCorpusMatchesLabeledOracleWithoutRowPooling() {
        int count = 0;
        foreach (SearchCertificateFixtures.Fixture fixture in SearchCertificateFixtures.Fixtures()) {
            SearchCertificateOracle.Counts oracle = SearchCertificateOracle.Enumerate(
                fixture.Deck,
                fixture.Hand,
                fixture.Combos,
                fixture.Groups,
                fixture.Priority
            );
            foreach (PhysicalEnumerationMethod method in Methods) {
                WorkBudget budget = new(CalculationWorkPolicy.Unlimited);
                ExactCalculationResult result = Calculate(fixture, method, budget, out PostDrawCalculation calculation);
                AssertRows(result, oracle.Wins[1], oracle.Outcomes, fixture.Name + "/" + method);
                Assert.That(
                    calculation.ReducedRows,
                    Is.EqualTo(fixture.Deck.Count(card => card.Active && card.Copies > 0)),
                    fixture.Name + ": pure batch attribution retains every original entry"
                );
                Assert.That(budget.PeakCells, Is.LessThanOrEqualTo(CalculationStoragePolicy.Standard.CellLimit));
                Assert.That(budget.PeakEntries, Is.LessThanOrEqualTo(CalculationStoragePolicy.Standard.EntryLimit));
            }
            count++;
        }
        TestContext.Out.WriteLine(count + " systematic/seeded fixtures matched in streaming, batch and frontier routes.");
    }

    [Test]
    public void CompletePhysicalMassPreservesQueuedDuplicatesCostsHiddenLossAndFailure() {
        bool sawFailure = false;
        foreach (SearchCertificateFixtures.Fixture fixture in BoundaryFixtures()) {
            Distribution oracle = EnumeratePhysicalDistribution(fixture);
            sawFailure |= oracle.Failed > 0;
            Dictionary<string, BigInteger>? reference = null;
            foreach (PhysicalEnumerationMethod method in Methods) {
                Dictionary<string, BigInteger> full = new(StringComparer.Ordinal);
                Dictionary<string, BigInteger> physical = new(StringComparer.Ordinal);
                WorkBudget budget = new(CalculationWorkPolicy.Unlimited);
                Calculate(
                    fixture,
                    method,
                    budget,
                    out _,
                    (rows, state, available, mass) => {
                        OrderedDrawStateLayout layout = new(rows);
                        Assert.That(state[layout.DrawsLeft], Is.Zero);
                        Assert.That(state[layout.CompletionSource], Is.Zero);
                        int hidden = layout.HasHiddenCount ? state[layout.HiddenCount] : 0;
                        Assert.That(state.Take(rows.Count).Sum() - available.Sum(), Is.EqualTo(hidden));
                        string[] ids = rows.Select(card => card.Id).Order(StringComparer.Ordinal).ToArray();
                        string complete = hidden + "|" + (layout.HasActivationState ? state[layout.ActivationState] : -1)
                            + "|" + string.Join(
                                ";",
                                ids.Select(id => {
                                    int row = rows.ToList().FindIndex(card => card.Id == id);
                                    int opt = layout.OptSlot(row);
                                    return id + ":" + state[row] + "," + state[layout.HandStart + row] + ","
                                        + available[row] + "," + (opt < 0 ? -1 : state[opt]);
                                })
                            ) + "|" + string.Join(",", state.Skip(layout.QueueStart).Select(row => rows[row].Id));
                        string observed = PhysicalKey(rows, state.AsSpan(layout.HandStart, rows.Count).ToArray(), available);
                        Add(full, complete, mass);
                        Add(physical, observed, mass);
                        // Audit buffers must not expose the live resolver/search state.
                        Array.Fill(state, int.MinValue);
                        Array.Fill(available, int.MinValue);
                    }
                );
                Assert.That(physical, Is.EquivalentTo(oracle.Physical), fixture.Name + "/" + method + "/physical mass");
                Assert.That(
                    physical.Values.Aggregate(BigInteger.Zero, (sum, mass) => sum + mass) + oracle.Failed,
                    Is.EqualTo(oracle.Outcomes),
                    fixture.Name + "/" + method + "/failed mass"
                );
                if (reference is null) {
                    reference = full;
                }
                else {
                    Assert.That(full, Is.EquivalentTo(reference), fixture.Name + "/" + method + "/complete terminal state");
                }
            }
        }
        Assert.That(sawFailure, Is.True, "The mandatory-removal fixture must exercise failed outcomes.");
    }

    [Test]
    public void FirstOnlySourceOrderRemainsObservableEvenWithOrdinaryShuffles() {
        CategoryBase target = new("Target");
        Card first = new([], 1, id: "first", drawCount: 1, firstActivationOnly: true);
        Card later = new([], 1, id: "later", drawCount: 1);
        Card t = new([target], 1, id: "t");
        Card f = new([], 1, id: "f");
        List<Combo> combos = [new([], "Filler", cards: [new("f", 1, 4)])];
        List<Card>? firstThenLater = DrawSequenceOracle.Resolve([first, later, t, f], 2, combos);
        List<Card>? laterThenFirst = DrawSequenceOracle.Resolve([later, first, t, f], 2, combos);
        Assert.That(firstThenLater!.Select(card => card.Id), Is.EquivalentTo(new[] { "t", "f" }));
        Assert.That(laterThenFirst!.Select(card => card.Id), Is.EquivalentTo(new[] { "first", "t" }));
    }

    [TestCase(0), TestCase(1), TestCase(2), TestCase(3), TestCase(4), TestCase(5)]
    public void EmptyAndExhaustionSensitiveHandsPreserveAllPhysicalOutcomes(int handSize) {
        CategoryBase target = new("Target");
        SearchCertificateFixtures.Fixture fixture = new(
            "hand-" + handSize,
            [
                new([], 2, id: "d", drawCount: 2, drawOncePerTurn: false),
                new([], 1, id: "s", search: new(CardSelector.For(target), false)),
                new([target], 2, id: "t"), new([], 1, id: "f")
            ],
            handSize,
            [new([new(target, 1, 2)]), new([new(target, 0, 0)])],
            [new("empty", "Empty")]
        );
        SearchCertificateOracle.Counts oracle = SearchCertificateOracle.Enumerate(
            fixture.Deck,
            fixture.Hand,
            fixture.Combos,
            fixture.Groups
        );
        foreach (PhysicalEnumerationMethod method in Methods) {
            AssertRows(
                Calculate(fixture, method, new(CalculationWorkPolicy.Unlimited), out _),
                oracle.Wins[1],
                oracle.Outcomes,
                fixture.Name + "/" + method
            );
        }
    }

    [Test]
    public void HigherEntryAllowanceAndNewlyMetHandMinimumMatchIndependentSimpleDrawOracle() {
        CategoryBase target = new("Target");
        SearchCertificateFixtures.Fixture fixture = new(
            "bounded-draws",
            [
                new([], 3, "same name", id: "d", drawCount: 2, oncePerTurn: false, activationLimit: 2),
                new([], 1, "same name", id: "e", drawCount: 1, minimumHandSize: 3),
                new([target], 1, id: "t"), new([], 3, id: "f")
            ],
            2,
            [
                new([new(target, 1, 1)], groupId: "g"), new([new(target, 0, 0)], groupId: "g"),
                new([], "Source remains", cards: [new("d", 1, 3)])
            ],
            [new("g", "Either"), new("empty", "Empty")]
        );
        Distribution oracle = EnumeratePhysicalDistribution(fixture, boundedDraws: true);
        foreach (PhysicalEnumerationMethod method in Methods) {
            Dictionary<string, BigInteger> physical = new(StringComparer.Ordinal);
            ExactCalculationResult result = Calculate(
                fixture,
                method,
                new(CalculationWorkPolicy.Unlimited),
                out _,
                (rows, state, available, mass) => Add(
                    physical,
                    PhysicalKey(rows, state.AsSpan(rows.Count, rows.Count).ToArray(), available),
                    mass
                )
            );
            Assert.That(physical, Is.EquivalentTo(oracle.Physical), method + "/bounded draw physical mass");
            AssertRows(result, oracle.NoSearchWins, oracle.Outcomes, method + "/bounded draw rows");
        }
    }

    [Test]
    public void FiniteWorkChargesActualBatchComputationAtItsExactBoundary() {
        SearchCertificateFixtures.Fixture fixture = ResourceFixture();
        WorkBudget measured = new(CalculationWorkPolicy.Unlimited);
        ExactCalculationResult expected = Calculate(fixture, PhysicalEnumerationMethod.BatchStreaming, measured, out _);
        Assert.That(measured.Spent, Is.GreaterThan(1));
        WorkBudget enough = new(new(measured.Spent));
        ExactCalculationResult actual = Calculate(fixture, PhysicalEnumerationMethod.BatchStreaming, enough, out _);
        Assert.That(Rows(actual), Is.EqualTo(Rows(expected)));
        Assert.That(enough.Spent, Is.EqualTo(measured.Spent));
        WorkBudget insufficient = new(new(measured.Spent - 1));
        ProbabilityCalculationLimitException failure = Assert.Throws<ProbabilityCalculationLimitException>(
            () => Calculate(fixture, PhysicalEnumerationMethod.BatchStreaming, insufficient, out _)
        )!;
        Assert.That(failure.Reason, Is.EqualTo(ProbabilityCalculationLimitReason.Work));
        Assert.That(insufficient.Spent, Is.LessThanOrEqualTo(measured.Spent - 1));
    }

    [Test]
    public void RetainedBatchCursorsAndIntegerPayloadHonorSmallEntryAndCellLimits() {
        SearchCertificateFixtures.Fixture fixture = ResourceFixture();
        WorkBudget measured = new(CalculationWorkPolicy.Unlimited);
        ExactCalculationResult expected = Calculate(
            fixture,
            PhysicalEnumerationMethod.BatchStreaming,
            measured,
            out PostDrawCalculation execution
        );
        Assert.That(execution.LargestStructure, Is.EqualTo("physical batch enumeration stack"));
        Assert.That(execution.PeakCells, Is.EqualTo(measured.PeakCells));
        CalculationStoragePolicy exact = SmallStorage(measured.PeakEntries, measured.PeakCells);
        ExactCalculationResult actual = Calculate(
            fixture,
            PhysicalEnumerationMethod.BatchStreaming,
            new(CalculationWorkPolicy.Unlimited, exact),
            out _
        );
        Assert.That(Rows(actual), Is.EqualTo(Rows(expected)));
        foreach (bool cells in new[] { false, true }) {
            CalculationStoragePolicy limited = SmallStorage(
                measured.PeakEntries - (cells ? 0 : 1),
                measured.PeakCells - (cells ? 1 : 0)
            );
            WorkBudget budget = new(CalculationWorkPolicy.Unlimited, limited);
            PostDrawCalculation rejected = null!;
            ProbabilityCalculationLimitException failure = Assert.Throws<ProbabilityCalculationLimitException>(
                () => Calculate(fixture, PhysicalEnumerationMethod.BatchStreaming, budget, out rejected)
            )!;
            Assert.That(failure.Reason, Is.EqualTo(ProbabilityCalculationLimitReason.Storage));
            Assert.That(rejected.LargestStructure, Is.EqualTo("physical batch enumeration stack"));
        }
    }

    [Test]
    public void BatchQuotientAvoidsReplayingDistinctOrdinaryOpeningOrders() {
        CategoryBase target = new("Target");
        List<Card> deck = Enumerable.Range(0, 7).Select(index => new Card([target], 1, id: "t" + index)).ToList();
        deck.Add(new([], 1, id: "d", drawCount: 2));
        SearchCertificateFixtures.Fixture fixture = new(
            "distinct-opening",
            deck,
            5,
            [new([new(target, 4, 6)]), new([], cards: [new("t0", 1, 6)])],
            []
        );
        WorkBudget original = new(CalculationWorkPolicy.Unlimited);
        WorkBudget batch = new(CalculationWorkPolicy.Unlimited);
        ExactCalculationResult expected = Calculate(fixture, PhysicalEnumerationMethod.Streaming, original, out _);
        ExactCalculationResult actual = Calculate(fixture, PhysicalEnumerationMethod.BatchStreaming, batch, out _);
        Assert.That(Rows(actual), Is.EqualTo(Rows(expected)));
        Assert.That(batch.Spent, Is.LessThan(original.Spent));
    }

    private static PhysicalEnumerationMethod[] Methods =>
        [PhysicalEnumerationMethod.Streaming, PhysicalEnumerationMethod.BatchStreaming, PhysicalEnumerationMethod.Frontier];

    private static ExactCalculationResult Calculate(
        SearchCertificateFixtures.Fixture fixture,
        PhysicalEnumerationMethod method,
        WorkBudget budget,
        out PostDrawCalculation calculation,
        Action<IReadOnlyList<Card>, int[], int[], BigInteger>? observer = null
    ) {
        List<Card> cards = fixture.Deck.Where(card => card.Active && card.Copies > 0).ToList();
        DrawActivationPriority? priority = fixture.Priority is null ? null : new(DrawActivationMode.CustomPriority, fixture.Priority);
        calculation = new(cards, fixture.Hand, budget, priority, deferIdentities: false) {
            ObservePhysicalOutcome = observer
        };
        return calculation.CalculateSearch(
            fixture.Combos,
            fixture.Groups,
            false,
            new(cards, budget),
            physical: true,
            method: method,
            coalesceOrdinary: false
        );
    }

    private static IEnumerable<SearchCertificateFixtures.Fixture> BoundaryFixtures() {
        CategoryBase target = new("Target");
        CategoryBase material = new("Material");
        List<Combo> combos = [new([new(target, 1, 2)], groupId: "g"), new([new(target, 0, 0)], groupId: "g"),
            new([], "No filler", cards: [new("f", 0, 0)]), new([], "Completed branch", groupId: "complete")];
        List<ComboGroup> groups = [new("g", "Target alternatives"), new("complete", "Completed"), new("empty", "Empty")];
        yield return new(
            "already-queued-max-one-and-newly-payable-cost",
            [
                new(
                    [],
                    2,
                    "duplicate",
                    id: "d",
                    drawCount: 1,
                    preDrawRemovalCount: 1,
                    preDrawSelector: CardSelector.For(material)
                ),
                new([], 1, "duplicate", id: "e", drawCount: 2), new([material], 1, id: "m"),
                new([target], 1, id: "t"), new([], 1, id: "s", search: new(CardSelector.For(target), false)),
                new([], 2, id: "f")
            ],
            2,
            combos,
            groups
        );
        yield return new(
            "same-batch-max-one-duplicates",
            [
                new([], 2, id: "d", drawCount: 1), new([], 1, id: "e", drawCount: 2),
                new([target], 1, id: "t"), new([], 1, id: "s", search: new(CardSelector.For(target), false)),
                new([], 2, id: "f")
            ],
            2,
            combos,
            groups,
            ["e", "d"]
        );
        foreach (string[]? priority in new string[]?[] { null, ["d", "e"], ["e", "d"], ["s"] }) {
            yield return new(
                "first-only-effect-order-" + (priority is null ? "encounter" : string.Join("-", priority)),
                [
                    new([], 1, "same", id: "d", drawCount: 1, firstActivationOnly: true),
                    new([], 1, "same", id: "e", drawCount: 1), new([target], 1, id: "t"),
                    new([], 1, id: "s", search: new(CardSelector.For(target), false)), new([], 2, id: "f")
                ],
                2,
                combos,
                groups,
                priority
            );
        }
        yield return new(
            "hidden-pre-post-and-mandatory-failure",
            [
                new(
                    [],
                    1,
                    id: "d",
                    drawCount: 1,
                    preDrawDeckRemovalCount: 1,
                    postDrawDeckMillCount: 1,
                    postDrawDiscardCount: 1,
                    postDrawSelector: CardSelector.For(target)
                ),
                new([], 1, id: "e", drawCount: 1, blocksLaterDraws: true), new([target], 1, id: "t"),
                new([], 1, id: "s", search: new(CardSelector.For(target), false)), new([], 3, id: "f")
            ],
            2,
            combos,
            groups,
            ["d", "e"]
        );
    }

    private static SearchCertificateFixtures.Fixture ResourceFixture() {
        CategoryBase target = new("Target");
        return new(
            "deep-small-stack",
            [
                new([], 8, id: "d", drawCount: 1, drawOncePerTurn: false),
                new([target], 1, id: "t"), new([], 1, id: "f"),
                new([], 1, id: "s", search: new(CardSelector.For(target), false))
            ],
            1,
            [new([new(target, 1, 1)])],
            []
        );
    }

    private static Distribution EnumeratePhysicalDistribution(SearchCertificateFixtures.Fixture fixture, bool boundedDraws = false) {
        List<Card> cards = fixture.Deck.Where(card => card.Active && card.Copies > 0).ToList();
        Card[] labels = cards.SelectMany(card => Enumerable.Repeat(card, card.Copies)).ToArray();
        int[] order = Enumerable.Range(0, labels.Length).ToArray();
        Dictionary<string, BigInteger> distribution = new(StringComparer.Ordinal);
        BigInteger total = 0;
        BigInteger failed = 0;
        BigInteger[] wins = new BigInteger[1 + fixture.Combos.Count + fixture.Groups.Count];
        Permute(0);
        return new(distribution, total, failed, wins);

        void Permute(int position) {
            if (position < order.Length) {
                for (int next = position; next < order.Length; next++) {
                    (order[position], order[next]) = (order[next], order[position]);
                    Permute(position + 1);
                    (order[position], order[next]) = (order[next], order[position]);
                }
                return;
            }
            total++;
            Card[] ordered = order.Select(index => labels[index]).ToArray();
            IReadOnlyList<Card> rest = [];
            List<Card>? hand;
            if (boundedDraws) {
                (hand, rest) = ResolveBoundedDraws(ordered, fixture.Hand);
            }
            else {
                hand = DrawSequenceOracle.Resolve(ordered, fixture.Hand, fixture.Combos, fixture.Priority, remaining => rest = remaining);
            }
            if (hand is null) {
                failed++;
                return;
            }
            int[] held = cards.Select(card => hand.Count(candidate => candidate.Id == card.Id)).ToArray();
            int[] available = cards.Select(card => rest.Count(candidate => candidate.Id == card.Id)).ToArray();
            Add(distribution, PhysicalKey(cards, held, available), BigInteger.One);
            bool[] individual = fixture.Combos.Select(combo => SmallDeckOracle.MatchesHand(hand, combo)).ToArray();
            if (individual.Any(value => value)) {
                wins[0]++;
            }
            for (int combo = 0; combo < individual.Length; combo++) {
                if (individual[combo]) {
                    wins[1 + combo]++;
                }
            }
            for (int group = 0; group < fixture.Groups.Count; group++) {
                if (fixture.Combos.Where((combo, index) => individual[index] && combo.GroupId == fixture.Groups[group].Id).Any()) {
                    wins[1 + individual.Length + group]++;
                }
            }
        }
    }

    // Independent labeled FIFO resolver for the no-cost/no-removal bounded
    // fixture. It deliberately implements numeric allowances and hand minima,
    // which the older general DrawSequenceOracle does not currently model.
    private static (List<Card> Hand, IReadOnlyList<Card> Remaining) ResolveBoundedDraws(Card[] order, int handSize) {
        List<int> hand = Enumerable.Range(0, handSize).ToList();
        List<int> queue = hand.Where(label => order[label].DrawCount is not null).ToList();
        Dictionary<string, int> used = new(StringComparer.Ordinal);
        int next = handSize;
        while (true) {
            queue.RemoveAll(
                label => used.GetValueOrDefault(order[label].Id) >= order[label].MaximumActivations
                    || order[label].DrawCount!.Value > order.Length - next
            );
            int source = queue.FirstOrDefault(label => hand.Count >= order[label].MinimumHandSize, -1);
            if (source < 0) {
                return (hand.Select(label => order[label]).ToList(), order.Skip(next).ToArray());
            }
            queue.Remove(source);
            hand.Remove(source);
            used[order[source].Id] = used.GetValueOrDefault(order[source].Id) + 1;
            for (int draw = 0; draw < order[source].DrawCount!.Value; draw++) {
                hand.Add(next);
                if (order[next].DrawCount is not null) {
                    queue.Add(next);
                }
                next++;
            }
        }
    }

    private static CalculationStoragePolicy SmallStorage(long entries, long cells) {
        // Keep unsupported test limits out of the production API and wire schema.
        ConstructorInfo constructor = typeof(CalculationStoragePolicy).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            [typeof(CalculationStorageTier), typeof(long), typeof(long)],
            null
        )!;
        return (CalculationStoragePolicy)constructor.Invoke([CalculationStorageTier.Standard, entries, cells]);
    }

    private static string PhysicalKey(IReadOnlyList<Card> rows, int[] hand, int[] available) =>
        string.Join(
            ";",
            rows.Select((card, row) => (card.Id, Held: hand[row], Available: available[row]))
                .OrderBy(entry => entry.Id, StringComparer.Ordinal)
                .Select(entry => entry.Id + ":" + entry.Held + "," + entry.Available)
        );

    private static void Add(Dictionary<string, BigInteger> distribution, string key, BigInteger mass) =>
        distribution[key] = distribution.GetValueOrDefault(key) + mass;

    private static ExactProbability[] Rows(ExactCalculationResult result) => [result.Total, .. result.Combos, .. result.Groups];

    private static void AssertRows(ExactCalculationResult result, BigInteger[] wins, BigInteger denominator, string context) {
        ExactProbability[] rows = Rows(result);
        Assert.That(rows.Length, Is.EqualTo(wins.Length), context);
        for (int row = 0; row < rows.Length; row++) {
            Assert.That(rows[row].Numerator * denominator, Is.EqualTo(wins[row] * rows[row].Denominator), context + "/" + row);
        }
    }
}
