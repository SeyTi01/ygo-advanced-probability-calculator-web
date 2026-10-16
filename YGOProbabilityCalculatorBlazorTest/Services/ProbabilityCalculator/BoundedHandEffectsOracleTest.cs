using System.Numerics;
using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture, NonParallelizable]
public sealed class BoundedHandEffectsOracleTest {
    private sealed record CategoryBound(CategoryBase Category, int Minimum, int Maximum);
    private sealed record CardBound(string CardId, int Minimum, int Maximum);
    private sealed record Requirement(Func<Card, bool> Includes, int Minimum, int Maximum);
    private sealed record OracleCombo(Combo Model, IReadOnlyList<Requirement> Requirements);
    private sealed record RemovalChoice(int[] Hand, int[] Original);
    private sealed record Rational(BigInteger Numerator, BigInteger Denominator);
    private sealed record OracleCounts(BigInteger Outcomes, BigInteger[] Wins);
    private sealed record OracleMode(CardSelector Selector, int? ActivationLimit);

    [TestCase(0)]
    [TestCase(32)]
    [TestCase(64)]
    public void DecodedBoundsMatchLabeledHandsAcrossMaskWidthsAndShrinkingHands(int fillerEntries) {
        CategoryBase a = new("A");
        CategoryBase b = new("B");
        CategoryBase absent = new("Absent");
        CategoryBase removable = new("Removable source");
        CardSelector aMode = CardSelector.For(a);
        CardSelector bMode = CardSelector.For(b);
        // Only one entry is removable, so the labeled oracle's removal choices
        // coincide with the deterministic policy for every generated hand.
        Card source = new([removable], 2, id: "source", search: new(CardSelector.AnyOfSelectors([aMode, bMode]), false,
            [new(aMode, 1), new(bMode, 1)]), activationLimit: 2,
            postDrawDiscardCount: 1, postDrawSelector: CardSelector.For(removable));
        List<Card> cards = [source];
        // Put matching rows beyond the requested word boundary, not just fillers.
        cards.AddRange(Enumerable.Range(0, fillerEntries).Select(index => new Card([], id: "filler-" + index)));
        cards.AddRange([new([a], id: "a"), new([a, b], id: "ab"), new([b], id: "b")]);
        List<OracleCombo> combos = [
            DefineCombo("Distinct overlapping roles", "positive", [new(a, 1, 2), new(b, 1, 2)]),
            DefineCombo("Zero maximum", "zero", [new(a, 0, 0)], [new(source.Id, 0, 0)]),
            DefineCombo("Empty positive mask", "positive", [new(absent, 1, 2)]),
            DefineCombo("Empty zero mask", "zero", [new(absent, 0, 0)], [new("b", 1, 1)])
        ];
        List<ComboGroup> groups = [new("positive", "Positive"), new("zero", "Zero maximum")];
        OracleCounts oracle = EnumerateLabeledHands(cards, 2, combos, groups);
        Rational[] expected = oracle.Wins.Select(win => new Rational(win, oracle.Outcomes)).ToArray();
        AssertMatchesOracle(cards, 2, combos, groups, expected, "Decoded masks with " + fillerEntries + " fillers");
    }

    [Test]
    public void BrandedOpeningConsumesItsSourceThenDiscardsOneOtherCardBeforeSearching() {
        CategoryBase sourceKind = new("Branded Opening");
        CategoryBase targetKind = new("Despia");
        CategoryBase fillerKind = new("Filler");
        Card source = new([sourceKind], id: "branded-opening", search: new(CardSelector.For(targetKind), false),
            preDrawRemovalCount: 1, preDrawSelector: CardSelector.Any, oncePerTurn: true);
        Card target = new([targetKind], 2, id: "despia-target");
        Card filler = new([fillerKind], 2, id: "filler");
        List<Card> deck = [source, target, filler];
        List<OracleCombo> combos = [DefineCombo("One target after the source and discard leave hand", "branded",
            [new(targetKind, 1, 1)], [new(source.Id, 0, 0), new(filler.Id, 0, 0)])];
        List<ComboGroup> groups = [new("branded", "Branded Opening")];

        AssertMatchesOracle(deck, 2, combos, groups, [new(2, 5), new(4, 10), new(4, 10)], "Branded Opening");
    }

    [Test]
    public void TractDiscardsTheFetchedLightFiendAfterSearch() {
        CategoryBase lightFiend = new("LIGHT Fiend");
        CategoryBase sourceKind = new("Fiendsmith's Tract");
        CategoryBase fillerKind = new("Filler");
        Card source = new([sourceKind], id: "tract", search: new(CardSelector.For(lightFiend), false),
            postDrawDiscardCount: 1, postDrawSelector: CardSelector.Any, oncePerTurn: true);
        Card originalTarget = new([lightFiend], id: "original-target");
        Card fetchedTarget = new([lightFiend], id: "fetched-target");
        Card filler = new([fillerKind], 2, id: "filler");
        List<Card> deck = [source, originalTarget, fetchedTarget, filler];
        List<OracleCombo> combos = [DefineCombo("Keep the original target after Tract resolves", "tract",
            [], [new(originalTarget.Id, 1, 1), new(source.Id, 0, 0), new(fetchedTarget.Id, 0, 0)])];
        List<ComboGroup> groups = [new("tract", "Fiendsmith's Tract")];

        AssertMatchesOracle(deck, 2, combos, groups, [new(3, 5), new(6, 10), new(6, 10)], "Fiendsmith's Tract");
    }

    [Test]
    public void ChantModesHaveIndependentPerEntryLimitsAndShareTheTwoActivationAllowance() {
        CategoryBase chantKind = new("Radiant Typhoon Chant");
        CategoryBase levelOneToFourRadiantTyphoon = new("Level 1-4 Radiant Typhoon Monster");
        CategoryBase mysticalSpaceTyphoon = new("Mystical Space Typhoon");
        CategoryBase fillerKind = new("Filler");
        CardSelector monsterMode = CardSelector.For(levelOneToFourRadiantTyphoon);
        CardSelector namedSpellMode = CardSelector.For(mysticalSpaceTyphoon);
        CardSelector modeUnion = CardSelector.AnyOfSelectors([monsterMode, namedSpellMode]);
        SearchEffectPlan chantSearch = new(modeUnion, false,
            [new(monsterMode, 1), new(namedSpellMode, 1)]);
        Card chant = new([chantKind], 2, id: "chant", search: chantSearch, oncePerTurn: false, activationLimit: 2);
        Card monster = new([levelOneToFourRadiantTyphoon], 2, id: "radiant-monster");
        Card namedSpell = new([mysticalSpaceTyphoon], id: "mystical-space-typhoon");
        Card filler = new([fillerKind], 2, id: "filler");
        List<Card> deck = [chant, monster, namedSpell, filler];
        List<OracleCombo> combos = [
            DefineCombo("One distinct target from each mode", "both-modes",
                [new(levelOneToFourRadiantTyphoon, 1, 1), new(mysticalSpaceTyphoon, 1, 1)], [new(chant.Id, 0, 0)]),
            DefineCombo("Two monsters cannot reuse the first mode", "two-monsters",
                [new(levelOneToFourRadiantTyphoon, 2, 2)], [new(chant.Id, 0, 0)])
        ];
        List<ComboGroup> groups = [new("both-modes", "Both modes"), new("two-monsters", "Monster mode limit")];

        AssertMatchesOracle(deck, 2, combos, groups,
            [new(10, 21), new(9, 21), new(5, 21), new(9, 21), new(5, 21)], "Radiant Typhoon Chant");
    }

    [Test]
    public void NumericTwoActivationLimitDiffersFromUnlimitedForThreeCopiesInHand() {
        CategoryBase sourceKind = new("Kewl Tune Synchro source");
        CategoryBase targetKind = new("Kewl Tune target");
        CategoryBase fillerKind = new("Filler");
        CardSelector targetSelector = CardSelector.For(targetKind);
        // A one-mode list routes both plans through the bounded physical oracle
        // while leaving the mode allowance unlimited. The card-level cap is the
        // only difference between these two plans.
        SearchEffectPlan search = new(targetSelector, false, [new(targetSelector)]);
        Card cappedSource = new([sourceKind], 3, id: "kewl-source-capped", search: search,
            oncePerTurn: false, activationLimit: 2);
        Card unlimitedSource = new([sourceKind], 3, id: "kewl-source-unlimited", search: search,
            oncePerTurn: false);
        Card target = new([targetKind], 3, id: "kewl-target");
        Card filler = new([fillerKind], id: "filler");
        List<OracleCombo> cappedCombo = [DefineCombo("Three Kewl Tune targets", "capped",
            [new(targetKind, 3, 3)])];
        List<OracleCombo> unlimitedCombo = [DefineCombo("Three Kewl Tune targets", "unlimited",
            [new(targetKind, 3, 3)])];

        AssertMatchesOracle([cappedSource, target, filler], 3, cappedCombo,
            [new("capped", "Two activations")],
            [new(19, 35), new(19, 35), new(19, 35)], "Kewl Tune Synchro activation limit two");
        AssertMatchesOracle([unlimitedSource, target, filler], 3, unlimitedCombo,
            [new("unlimited", "Unlimited activations")],
            [new(20, 35), new(20, 35), new(20, 35)], "Kewl Tune Synchro unlimited activations");
    }

    [Test]
    public void DistinctSearchEntriesWithTheSameDisplayNameHaveIndependentOncePerTurnLimits() {
        CategoryBase sourceKind = new("Search source");
        CategoryBase targetKind = new("Target");
        CategoryBase fillerKind = new("Filler");
        CardSelector targetSelector = CardSelector.For(targetKind);
        SearchEffectPlan search = new(targetSelector, false, [new(targetSelector)]);
        Card firstSource = new([sourceKind], id: "same-name-source-a", name: "Searcher",
            search: search, oncePerTurn: true);
        Card secondSource = new([sourceKind], id: "same-name-source-b", name: "Searcher",
            search: search, oncePerTurn: true);
        Card target = new([targetKind], 2, id: "target");
        Card filler = new([fillerKind], 2, id: "filler");
        List<OracleCombo> combos = [DefineCombo("Two target copies", "same-name",
            [new(targetKind, 2, 2)])];
        List<ComboGroup> groups = [new("same-name", "Same display name")];

        AssertMatchesOracle([firstSource, secondSource, target, filler], 2, combos, groups,
            [new(6, 15), new(6, 15), new(6, 15)], "Independent same-name search entries");
    }

    [Test]
    public void FetchedEffectSourceIsInertUntilItWasOriginallyInTheOpeningHand() {
        CategoryBase tutorKind = new("Tutor");
        CategoryBase chantKind = new("Radiant Typhoon Chant");
        CategoryBase firstTarget = new("Mode A");
        CategoryBase secondTarget = new("Mode B");
        CategoryBase fillerKind = new("Filler");
        CardSelector firstMode = CardSelector.For(firstTarget);
        CardSelector secondMode = CardSelector.For(secondTarget);
        CardSelector chantUnion = CardSelector.AnyOfSelectors([firstMode, secondMode]);
        Card chant = new([chantKind], id: "chant", search: new(chantUnion, false,
            [new(firstMode, 1), new(secondMode, 1)]), oncePerTurn: false, activationLimit: 2);
        Card tutor = new([tutorKind], id: "tutor", search: new(CardSelector.For(chantKind), false),
            oncePerTurn: false, activationLimit: 1);
        Card target = new([firstTarget], id: "mode-a-target");
        Card otherTarget = new([secondTarget], id: "mode-b-target");
        Card filler = new([fillerKind], id: "filler");
        List<Card> deck = [tutor, chant, target, otherTarget, filler];
        List<OracleCombo> combos = [DefineCombo("Mode A target", "original-source",
            [new(firstTarget, 1, 1)])];
        List<ComboGroup> groups = [new("original-source", "Original source")];

        AssertMatchesOracle(deck, 1, combos, groups, [new(2, 5), new(2, 5), new(2, 5)], "Fetched source is inert");
    }

    [Test]
    public void NoTargetAndOneOverlappingPhysicalCopyCannotSatisfyBothPositiveRoles() {
        CategoryBase sourceKind = new("Search source");
        CategoryBase unavailableTarget = new("Unavailable target");
        CategoryBase costKind = new("Cost");
        CategoryBase fillerKind = new("Filler");
        CardSelector unavailableSelector = CardSelector.For(unavailableTarget);
        Card noTargetSource = new([sourceKind], id: "no-target-source",
            search: new(unavailableSelector, false, [new(unavailableSelector, 1)]),
            preDrawRemovalCount: 1, preDrawSelector: CardSelector.For(costKind), oncePerTurn: false);
        Card absent = new([unavailableTarget], 0, id: "absent-target");
        Card cost = new([costKind], id: "cost");
        Card filler = new([fillerKind], 2, id: "filler");
        List<Card> noTargetDeck = [noTargetSource, absent, cost, filler];
        List<OracleCombo> missingTarget = [DefineCombo("Unavailable target", "no-target",
            [new(unavailableTarget, 1, 1)])];
        List<ComboGroup> noTargetGroups = [new("no-target", "No target")];
        AssertMatchesOracle(noTargetDeck, 2, missingTarget, noTargetGroups,
            [new(0, 1), new(0, 1), new(0, 1)], "No legal target in the deck");

        CategoryBase firstRole = new("Role A");
        CategoryBase secondRole = new("Role B");
        CategoryBase dualTarget = new("Overlapping target");
        CardSelector roleA = CardSelector.For(firstRole);
        CardSelector roleB = CardSelector.For(secondRole);
        CardSelector eitherRole = CardSelector.AnyOfSelectors([roleA, roleB]);
        Card dualRoleSource = new([sourceKind], id: "dual-role-source",
            search: new(eitherRole, false, [new(roleA, 1), new(roleB, 1)]), oncePerTurn: false, activationLimit: 2);
        Card oneDualCopy = new([firstRole, secondRole, dualTarget], id: "one-dual-copy");
        Card twoFillers = new([fillerKind], 2, id: "two-fillers");
        List<Card> distinctDeck = [dualRoleSource, oneDualCopy, twoFillers];
        List<OracleCombo> distinctRoles = [DefineCombo("Distinct physical A and B copies", "distinct",
            [new(firstRole, 1, 1), new(secondRole, 1, 1)])];
        List<ComboGroup> distinctGroups = [new("distinct", "Distinct physical copies")];
        AssertMatchesOracle(distinctDeck, 1, distinctRoles, distinctGroups,
            [new(0, 1), new(0, 1), new(0, 1)], "One dual target cannot fill both minima");
    }

    private static OracleCombo DefineCombo(string name, string? groupId,
        IEnumerable<CategoryBound>? categories = null, IEnumerable<CardBound>? directCards = null) {
        List<CategoryBound> categoryBounds = categories?.ToList() ?? [];
        List<CardBound> cardBounds = directCards?.ToList() ?? [];
        Combo model = new(categoryBounds.Select(bound => new ComboCategory(bound.Category, bound.Minimum, bound.Maximum)), name,
            groupId: groupId, cards: cardBounds.Select(bound => new ComboCard(bound.CardId, bound.Minimum, bound.Maximum)));
        List<Requirement> requirements = [
            .. categoryBounds.Select(bound => new Requirement(
                card => card.Categories.Any(category => category.Identity == bound.Category.Identity), bound.Minimum, bound.Maximum)),
            .. cardBounds.Select(bound => new Requirement(card => card.Id == bound.CardId, bound.Minimum, bound.Maximum))
        ];

        return new(model, requirements);
    }

    private static void AssertMatchesOracle(List<Card> deck, int handSize, IReadOnlyList<OracleCombo> definitions,
        IReadOnlyList<ComboGroup> groups, IReadOnlyList<Rational> expectedRows, string description) {
        OracleCounts oracle = EnumerateLabeledHands(deck, handSize, definitions, groups);
        Assert.That(expectedRows, Has.Count.EqualTo(oracle.Wins.Length), description + " expected row count");
        Assert.That(oracle.Outcomes, Is.GreaterThan(BigInteger.Zero), description + " labeled outcomes");
        for (int row = 0; row < oracle.Wins.Length; row++) {
            AssertRational(oracle.Wins[row], oracle.Outcomes, expectedRows[row], description + " oracle row " + row);
        }

        List<Combo> combos = definitions.Select(definition => definition.Model).ToList();
        ProbabilityCalculatorService calculator = new();
        foreach (CalculationModel mode in new[] { CalculationModel.Fast, CalculationModel.Exact }) {
            ProbabilityCalculationResult result = calculator.CalculateProbabilityResults(deck, combos, handSize, groups,
                CalculationWorkPolicy.Interactive, options: new(Mode: mode));
            CalculationSnapshot snapshot = CalculationSnapshot.Capture(deck, combos, handSize, groups, options: new(Mode: mode));
            ProbabilityCalculationResult wire = CalculationWire.ReadResult(CalculationWire.Execute(snapshot.Json), mode);
            Assert.That(wire.Contract!.Fractions!.Total, Is.EqualTo(result.Contract!.Fractions!.Total), description + " worker total");
            Assert.That(wire.Contract.Fractions.Combos, Is.EqualTo(result.Contract.Fractions.Combos), description + " worker combos");
            Assert.That(wire.Contract.Fractions.Groups, Is.EqualTo(result.Contract.Fractions.Groups), description + " worker groups");
            Assert.That(result.Contract!.Accuracy, Is.EqualTo(CalculationAccuracy.Exact), description + " " + mode);
            Assert.That(result.Contract.Fractions, Is.Not.Null, description + " exact fractions");
            ProbabilityFraction[] rows = [result.Contract.Fractions!.Total, .. result.Contract.Fractions.Combos,
                .. result.Contract.Fractions.Groups];
            Assert.That(rows, Has.Length.EqualTo(oracle.Wins.Length), description + " result row count");
            for (int row = 0; row < rows.Length; row++) {
                AssertFraction(rows[row], oracle.Wins[row], oracle.Outcomes, description + " " + mode + " row " + row);
                AssertFraction(rows[row], expectedRows[row], description + " hard-coded " + mode + " row " + row);
            }
        }
    }

    private static OracleCounts EnumerateLabeledHands(List<Card> cards, int handSize,
        IReadOnlyList<OracleCombo> combos, IReadOnlyList<ComboGroup> groups) {
        int[] labels = Enumerable.Range(0, cards.Count)
            .Where(row => cards[row].Active && cards[row].Copies > 0)
            .SelectMany(row => Enumerable.Repeat(row, cards[row].Copies)).ToArray();
        if (handSize < 0 || handSize > labels.Length) {
            throw new ArgumentOutOfRangeException(nameof(handSize));
        }

        int[] hand = new int[cards.Count];
        BigInteger outcomes = BigInteger.Zero;
        BigInteger[] wins = new BigInteger[1 + combos.Count + groups.Count];
        Choose(0, handSize);

        return new(outcomes, wins);

        void Choose(int firstLabel, int remaining) {
            if (remaining == 0) {
                CountHand();
                return;
            }

            for (int label = firstLabel; label <= labels.Length - remaining; label++) {
                hand[labels[label]]++;
                Choose(label + 1, remaining - 1);
                hand[labels[label]]--;
            }
        }

        void CountHand() {
            outcomes++;
            int[] original = (int[])hand.Clone();
            int[] available = cards.Select(card => card.Active ? card.Copies : 0).ToArray();
            for (int row = 0; row < cards.Count; row++) {
                available[row] -= hand[row];
            }
            bool[] satisfied = EvaluateEffectChoices(cards, hand, original, available, combos);
            if (satisfied.Any(value => value)) {
                wins[0]++;
            }
            for (int combo = 0; combo < combos.Count; combo++) {
                if (satisfied[combo]) {
                    wins[combo + 1]++;
                }
            }
            for (int group = 0; group < groups.Count; group++) {
                if (combos.Select((combo, index) => (combo, index))
                    .Any(item => item.combo.Model.GroupId == groups[group].Id && satisfied[item.index])) {
                    wins[1 + combos.Count + group]++;
                }
            }
        }
    }

    private static bool[] EvaluateEffectChoices(List<Card> cards, int[] initialHand, int[] initialOriginal,
        int[] initialAvailable, IReadOnlyList<OracleCombo> combos) {
        bool[] satisfied = new bool[combos.Count];
        int[] usedActivations = new int[cards.Count];
        int[][] usedModes = cards.Select(card => new int[card.Search?.Modes?.Count ?? 1]).ToArray();
        Visit(initialHand, initialOriginal, initialAvailable, usedActivations, usedModes);

        return satisfied;

        void Visit(int[] hand, int[] original, int[] available, int[] used, int[][] modeUses) {
            for (int combo = 0; combo < combos.Count; combo++) {
                if (!satisfied[combo] && Satisfies(cards, hand, combos[combo])) {
                    satisfied[combo] = true;
                }
            }
            if (satisfied.All(value => value)) {
                return;
            }

            for (int source = 0; source < cards.Count; source++) {
                Card effect = cards[source];
                SearchEffectPlan? search = effect.Search;
                if (original[source] == 0 || search is null || used[source] >= effect.MaximumActivations
                    || hand.Sum() < effect.MinimumHandSize) {
                    continue;
                }

                OracleMode[] modes = search.Modes is { Count: > 0 }
                    ? search.Modes.Select(mode => new OracleMode(mode.Selector, mode.ActivationLimit)).ToArray()
                    : [new(search.Selector, null)];
                for (int mode = 0; mode < modes.Length; mode++) {
                    int modeLimit = modes[mode].ActivationLimit ?? int.MaxValue;
                    if (modeUses[source][mode] >= modeLimit) {
                        continue;
                    }

                    int[] consumedHand = (int[])hand.Clone();
                    int[] consumedOriginal = (int[])original.Clone();
                    consumedHand[source]--;
                    consumedOriginal[source]--;
                    foreach (RemovalChoice cost in RemoveEligible(cards, consumedHand, consumedOriginal,
                        effect.PreDrawRemovalCount, effect.PreDrawSelector)) {
                        for (int target = 0; target < cards.Count; target++) {
                            if (target == source || available[target] == 0 || !cards[target].Active
                                || !SelectorMatches(modes[mode].Selector, cards[target])) {
                                continue;
                            }

                            int[] nextAvailable = (int[])available.Clone();
                            nextAvailable[target]--;
                            int[] fetchedHand = (int[])cost.Hand.Clone();
                            int[] fetchedOriginal = (int[])cost.Original.Clone();
                            fetchedHand[target]++;
                            foreach (RemovalChoice afterSearch in RemoveEligible(cards, fetchedHand, fetchedOriginal,
                                effect.PostDrawDiscardCount, effect.PostDrawSelector)) {
                                int[] nextUsed = (int[])used.Clone();
                                nextUsed[source]++;
                                int[][] nextModeUses = modeUses.Select(uses => (int[])uses.Clone()).ToArray();
                                nextModeUses[source][mode]++;
                                Visit(afterSearch.Hand, afterSearch.Original, nextAvailable, nextUsed, nextModeUses);
                            }
                        }
                    }
                }
            }
        }
    }

    private static List<RemovalChoice> RemoveEligible(List<Card> cards, int[] hand, int[] original,
        int count, CardSelector selector) {
        List<RemovalChoice> choices = [];
        Choose(hand, original, count);

        return choices;

        void Choose(int[] currentHand, int[] currentOriginal, int remaining) {
            if (remaining == 0) {
                choices.Add(new((int[])currentHand.Clone(), (int[])currentOriginal.Clone()));
                return;
            }

            for (int row = 0; row < cards.Count; row++) {
                if (currentHand[row] == 0 || !SelectorMatches(selector, cards[row])) {
                    continue;
                }
                int previousHand = currentHand[row];
                int previousOriginal = currentOriginal[row];
                currentHand[row]--;
                // The source distinguishes original and fetched copies even when
                // they share an entry; an eligible fetched copy can be removed
                // while an originally held effect source remains available.
                currentOriginal[row] = Math.Min(currentOriginal[row], currentHand[row]);
                Choose(currentHand, currentOriginal, remaining - 1);
                currentHand[row] = previousHand;
                currentOriginal[row] = previousOriginal;
            }
        }
    }

    private static bool Satisfies(List<Card> cards, int[] hand, OracleCombo combo) {
        for (int requirement = 0; requirement < combo.Requirements.Count; requirement++) {
            Requirement bound = combo.Requirements[requirement];
            int count = 0;
            for (int row = 0; row < cards.Count; row++) {
                if (bound.Includes(cards[row])) {
                    count += hand[row];
                }
            }
            if (count < bound.Minimum || count > bound.Maximum) {
                return false;
            }
        }

        List<int> labels = [];
        for (int row = 0; row < hand.Length; row++) {
            labels.AddRange(Enumerable.Repeat(row, hand[row]));
        }
        int[] slots = combo.Requirements.SelectMany((requirement, index) =>
            Enumerable.Repeat(index, requirement.Minimum)).ToArray();
        bool[] assigned = new bool[labels.Count];
        return Assign(0);

        bool Assign(int slot) {
            if (slot == slots.Length) {
                return true;
            }
            Requirement required = combo.Requirements[slots[slot]];
            for (int label = 0; label < labels.Count; label++) {
                if (assigned[label] || !required.Includes(cards[labels[label]])) {
                    continue;
                }
                assigned[label] = true;
                if (Assign(slot + 1)) {
                    return true;
                }
                assigned[label] = false;
            }

            return false;
        }
    }

    private static bool SelectorMatches(CardSelector selector, Card card) {
        bool HasCategory(string identity) => card.Categories.Any(category => category.Identity == identity);

        return selector.Kind switch {
            CardSelectorKind.Any => true,
            CardSelectorKind.Category => HasCategory(selector.CategoryIdentities[0]),
            CardSelectorKind.AnyOf when selector.Children.Count > 0 => selector.Children.Any(child => SelectorMatches(child, card)),
            CardSelectorKind.AnyOf => selector.CategoryIdentities.Any(HasCategory),
            CardSelectorKind.AllOf when selector.Children.Count > 0 => selector.Children.All(child => SelectorMatches(child, card)),
            CardSelectorKind.AllOf => selector.CategoryIdentities.All(HasCategory),
            _ => throw new NotSupportedException("Oracle fixtures use only direct category selectors.")
        };
    }

    private static void AssertRational(BigInteger actualNumerator, BigInteger actualDenominator,
        Rational expected, string description) {
        Assert.That(actualNumerator * expected.Denominator, Is.EqualTo(expected.Numerator * actualDenominator), description);
    }

    private static void AssertFraction(ProbabilityFraction actual, BigInteger numerator, BigInteger denominator, string description) =>
        Assert.That(BigInteger.Parse(actual.Numerator) * denominator,
            Is.EqualTo(numerator * BigInteger.Parse(actual.Denominator)), description);

    private static void AssertFraction(ProbabilityFraction actual, Rational expected, string description) =>
        AssertFraction(actual, expected.Numerator, expected.Denominator, description);
}
