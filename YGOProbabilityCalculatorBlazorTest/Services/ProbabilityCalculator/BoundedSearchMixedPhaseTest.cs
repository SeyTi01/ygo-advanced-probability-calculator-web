using System.Numerics;
using System.Text.Json;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture, NonParallelizable]
public sealed class BoundedSearchMixedPhaseTest {
    private sealed record OracleCounts(BigInteger Orders, BigInteger Wins,
        int HiddenTargetLossWitnesses, int FetchedDrawWitnesses, int MissingFinalTargetWitnesses);

    private sealed record OracleResolution(bool Success, bool HiddenTargetLostBeforeSearch,
        bool FetchedDrawRemainedInert, bool TargetMissingAfterFinalDraw);

    [Test]
    public void DrawOneNoOptRunsBeforeCustomPreCostSearchAndCannotRecoverAHiddenTarget() {
        CategoryBase targetKind = new("Search target");
        CategoryBase costKind = new("Search cost");
        CategoryBase drawKind = new("Draw effect");
        CardSelector targetSelector = CardSelector.For(targetKind);
        Card source = new([new("Search source")], id: "source",
            search: new(targetSelector, false), preDrawRemovalCount: 1,
            preDrawSelector: CardSelector.For(costKind));
        Card target = new([targetKind], id: "target");
        Card cost = new([costKind], id: "cost");
        // A Draw 1 non-OPT source first banishes one unknown top-deck card.
        // The search source's filtered hand cost forces the bounded search path.
        Card draw = new([drawKind], id: "draw", drawCount: 1, drawOncePerTurn: false,
            preDrawDeckRemovalCount: 1);
        Card filler = new([], 2, id: "filler");
        List<Card> deck = [source, target, cost, draw, filler];
        List<Combo> combos = [new([new(targetKind, 1, 1)], "One target")];

        OracleCounts oracle = EnumerateLabeledOrders(deck, 2, targetKind, requiredTargets: 1);
        Assert.That(oracle.Orders, Is.EqualTo(new BigInteger(720)));
        Assert.That(oracle.Wins, Is.EqualTo(new BigInteger(352)));
        Assert.That(oracle.HiddenTargetLossWitnesses, Is.EqualTo(24),
            "The source may be in the opener or drawn during Draw 1; 24 labeled orders hide the target and leave a source available before search.");

        AssertBothCalculationModesMatchOracle(deck, combos, 2, oracle, "hidden target before search");
    }

    [Test]
    public void DrawFetchedBySearchIsInertBecauseTheDrawPhaseHasAlreadyFinished() {
        CategoryBase targetKind = new("Combo target");
        CategoryBase costKind = new("Search cost");
        CategoryBase drawKind = new("Draw effect");
        CardSelector drawSelector = CardSelector.For(drawKind);
        Card source = new([new("Search source")], id: "source", search: new(drawSelector, false),
            preDrawRemovalCount: 1, preDrawSelector: CardSelector.For(costKind));
        Card cost = new([costKind], id: "cost");
        Card draw = new([drawKind], id: "draw", drawCount: 1, drawOncePerTurn: false);
        Card target = new([targetKind], id: "target");
        Card filler = new([], 2, id: "filler");
        List<Card> deck = [source, cost, draw, target, filler];
        List<Combo> combos = [new([new(targetKind, 1, 1)], "One target")];

        OracleCounts oracle = EnumerateLabeledOrders(deck, 2, targetKind, requiredTargets: 1);
        Assert.That(oracle.Orders, Is.EqualTo(new BigInteger(720)));
        Assert.That(oracle.Wins, Is.EqualTo(new BigInteger(288)));
        Assert.That(oracle.FetchedDrawWitnesses, Is.EqualTo(48),
            "Every labeled order with source and cost in the opener fetches the draw source after draws are complete.");

        AssertBothCalculationModesMatchOracle(deck, combos, 2, oracle, "fetched draw remains inert");
    }

    [Test]
    public void SearchCannotReuseTheOnlyTargetConsumedByTheFinalDraw() {
        CategoryBase targetKind = new("Target");
        CardSelector targetSelector = CardSelector.For(targetKind);
        Card source = new([new("Search source")], id: "source",
            search: new(targetSelector, false, [new(targetSelector, 1)]));
        Card draw = new([], id: "draw", drawCount: 1, drawOncePerTurn: false);
        Card target = new([targetKind], id: "target");
        List<Card> deck = [source, draw, target];
        List<Combo> combos = [new([new(targetKind, 2, 2)], "Two targets")];

        OracleCounts oracle = EnumerateLabeledOrders(deck, 2, targetKind, requiredTargets: 2);
        Assert.That(oracle.Orders, Is.EqualTo(new BigInteger(6)));
        Assert.That(oracle.Wins, Is.EqualTo(BigInteger.Zero));
        Assert.That(oracle.MissingFinalTargetWitnesses, Is.EqualTo(2),
            "Both orders beginning with the original search source and Draw 1 draw the last target, leaving no deck target.");

        AssertBothCalculationModesMatchOracle(deck, combos, 2, oracle, "target consumed by final draw");
    }

    [Test]
    public void WorkAndStorageLimitsRemainTypedWireFailuresInFastAndExactModes() {
        CategoryBase targetKind = new("Target");
        CategoryBase costKind = new("Cost");
        CardSelector targetSelector = CardSelector.For(targetKind);
        Card source = new([new("Source")], id: "source", search: new(targetSelector, false),
            preDrawRemovalCount: 1, preDrawSelector: CardSelector.For(costKind));
        List<Card> deck = [source, new([targetKind], 2, id: "target"), new([costKind], id: "cost"), new([], 2, id: "filler")];
        List<Combo> combos = [new([new(targetKind, 1, 2)], "Target")];
        List<ComboGroup> ordinaryGroups = [new("target", "Target")];
        List<ComboGroup> storageGroups = Enumerable.Range(0, 32_768)
            .Select(index => new ComboGroup("group-" + index, "Group")).ToList();

        foreach (CalculationModel mode in new[] { CalculationModel.Fast, CalculationModel.Exact }) {
            CalculationSnapshot workLimited = CalculationSnapshot.Capture(deck, combos, 2, ordinaryGroups,
                workPolicy: new(1), options: new(Mode: mode));
            AssertTypedLimit(workLimited, mode, ProbabilityCalculationLimitReason.Work, CalculationCompletion.WorkLimit);

            CalculationSnapshot storageLimited = CalculationSnapshot.Capture(deck, combos, 2, storageGroups,
                storagePolicy: CalculationStoragePolicy.Standard, options: new(Mode: mode));
            AssertTypedLimit(storageLimited, mode, ProbabilityCalculationLimitReason.Storage, CalculationCompletion.StorageLimit);
        }
    }

    private static void AssertBothCalculationModesMatchOracle(List<Card> deck, List<Combo> combos, int handSize,
        OracleCounts oracle, string description) {
        foreach (CalculationModel mode in new[] { CalculationModel.Fast, CalculationModel.Exact }) {
            ProbabilityCalculationResult result = new ProbabilityCalculatorService().CalculateProbabilityResults(
                deck, combos, handSize, [], CalculationWorkPolicy.Interactive, options: new(Mode: mode));
            Assert.That(result.Contract!.Accuracy, Is.EqualTo(CalculationAccuracy.Exact), description + " " + mode);
            Assert.That(result.SearchAvailability, Is.Null, description + " uses the bounded physical execution path");
            ProbabilityFraction[] rows = [result.Contract.Fractions!.Total, .. result.Contract.Fractions.Combos];
            Assert.That(rows, Has.Length.EqualTo(2), description + " rows");
            for (int row = 0; row < rows.Length; row++) {
                AssertFraction(rows[row], oracle.Wins, oracle.Orders, description + " " + mode + " row " + row);
            }
        }
    }

    private static OracleCounts EnumerateLabeledOrders(List<Card> deck, int handSize,
        CategoryBase targetKind, int requiredTargets) {
        int[] labels = deck.SelectMany((card, row) => Enumerable.Repeat(row, card.Copies)).ToArray();
        int[] order = Enumerable.Range(0, labels.Length).ToArray();
        BigInteger outcomes = BigInteger.Zero;
        BigInteger wins = BigInteger.Zero;
        int hiddenWitnesses = 0;
        int fetchedDrawWitnesses = 0;
        int missingFinalTargetWitnesses = 0;
        Permute(0);

        return new(outcomes, wins, hiddenWitnesses, fetchedDrawWitnesses, missingFinalTargetWitnesses);

        void Permute(int depth) {
            if (depth == order.Length) {
                outcomes++;
                OracleResolution resolution = ResolveOrder(order, labels, deck, handSize, targetKind, requiredTargets);
                if (resolution.Success) {
                    wins++;
                }
                if (resolution.HiddenTargetLostBeforeSearch) {
                    hiddenWitnesses++;
                }
                if (resolution.FetchedDrawRemainedInert) {
                    fetchedDrawWitnesses++;
                }
                if (resolution.TargetMissingAfterFinalDraw) {
                    missingFinalTargetWitnesses++;
                }
                return;
            }

            for (int next = depth; next < order.Length; next++) {
                (order[depth], order[next]) = (order[next], order[depth]);
                Permute(depth + 1);
                (order[depth], order[next]) = (order[next], order[depth]);
            }
        }
    }

    // This oracle enumerates every labeled physical permutation itself. It deals
    // the opener, resolves Draw1 effects and unknown top-deck losses in order,
    // then runs only searchers whose physical labels are in the pre-search hand.
    private static OracleResolution ResolveOrder(int[] order, int[] labels, List<Card> deck, int handSize,
        CategoryBase targetKind, int requiredTargets) {
        List<int> hand = order.Take(handSize).ToList();
        List<int> remaining = order.Skip(handSize).ToList();
        List<int> pendingDraws = hand.Where(label => deck[labels[label]].DrawCount is not null).ToList();
        HashSet<string> usedDrawEntries = new(StringComparer.Ordinal);
        int lastDrawn = -1;
        bool hiddenTargetRemoved = false;

        while (pendingDraws.Count > 0) {
            int sourceLabel = pendingDraws[0];
            pendingDraws.RemoveAt(0);
            if (!hand.Contains(sourceLabel)) {
                continue;
            }
            Card effect = deck[labels[sourceLabel]];
            if (effect.DrawOncePerTurn && usedDrawEntries.Contains(effect.Id)) {
                continue;
            }
            int hiddenCount = effect.PreDrawDeckRemovalCount;
            int drawCount = effect.DrawCount!.Value;
            int millCount = effect.PostDrawDeckMillCount;
            if (remaining.Count < hiddenCount + drawCount + millCount) {
                continue;
            }

            usedDrawEntries.Add(effect.Id);
            for (int hidden = 0; hidden < hiddenCount; hidden++) {
                int lost = remaining[0];
                hiddenTargetRemoved |= IsTarget(labels[lost]);
                remaining.RemoveAt(0);
            }
            hand.Remove(sourceLabel);
            for (int draw = 0; draw < drawCount; draw++) {
                int drawn = remaining[0];
                remaining.RemoveAt(0);
                hand.Add(drawn);
                lastDrawn = drawn;
                if (deck[labels[drawn]].DrawCount is not null) {
                    pendingDraws.Add(drawn);
                }
            }
            for (int mill = 0; mill < millCount; mill++) {
                remaining.RemoveAt(0);
            }
        }

        // Every physical card in hand now existed before search resolution,
        // including entries drawn by a Draw effect. Search-fetched cards are
        // added only below and therefore cannot become additional sources.
        HashSet<int> original = hand.ToHashSet();
        bool meetsBeforeSearch = Meets(hand);
        bool hasOriginalSearchSource = original.Any(label => hand.Contains(label) && deck[labels[label]].Search is not null);
        bool fetchedDrawInert = false;
        bool success = meetsBeforeSearch;
        if (!success && hasOriginalSearchSource) {
            foreach (int sourceLabel in original.Where(label => hand.Contains(label) && deck[labels[label]].Search is not null)) {
                Card source = deck[labels[sourceLabel]];
                SearchEffectPlan search = source.Search!;
                List<int> afterSource = [.. hand];
                afterSource.Remove(sourceLabel);
                List<List<int>> paidHands = RemoveEligible(afterSource, source.PreDrawRemovalCount, source.PreDrawSelector);
                foreach (List<int> paid in paidHands) {
                    foreach (int targetLabel in remaining) {
                        Card candidate = deck[labels[targetLabel]];
                        if (candidate.Id == source.Id || !SelectorMatches(search.Selector, candidate)) {
                            continue;
                        }
                        List<int> fetchedHand = [.. paid, targetLabel];
                        List<List<int>> afterDiscard = RemoveEligible(fetchedHand, source.PostDrawDiscardCount, source.PostDrawSelector);
                        foreach (List<int> resolved in afterDiscard) {
                            if (candidate.DrawCount is not null && !meetsBeforeSearch && !Meets(resolved)) {
                                // Search happens only after the draw queue above has ended.
                                fetchedDrawInert = true;
                            }
                            if (Meets(resolved)) {
                                success = true;
                            }
                        }
                    }
                }
            }
        }

        bool targetMissingAfterFinalDraw = hasOriginalSearchSource && lastDrawn >= 0
            && IsTarget(labels[lastDrawn]) && remaining.All(label => !IsTarget(labels[label])) && !meetsBeforeSearch;

        return new(success,
            hiddenTargetRemoved && hasOriginalSearchSource && remaining.All(label => !IsTarget(labels[label])) && !meetsBeforeSearch,
            fetchedDrawInert, targetMissingAfterFinalDraw);

        bool IsTarget(int row) => deck[row].Categories.Any(category => category.Identity == targetKind.Identity);

        bool Meets(IReadOnlyList<int> held) {
            int count = held.Count(label => IsTarget(labels[label]));
            return count == requiredTargets;
        }

        List<List<int>> RemoveEligible(List<int> held, int count, CardSelector selector) {
            List<List<int>> results = [];
            Choose(0, [.. held]);
            return results;

            void Choose(int removed, List<int> current) {
                if (removed == count) {
                    results.Add(current);
                    return;
                }
                for (int slot = 0; slot < current.Count; slot++) {
                    if (!SelectorMatches(selector, deck[labels[current[slot]]])) {
                        continue;
                    }
                    List<int> next = [.. current];
                    next.RemoveAt(slot);
                    Choose(removed + 1, next);
                }
            }
        }
    }

    private static bool SelectorMatches(CardSelector selector, Card card) => selector.Kind switch {
        CardSelectorKind.Any => true,
        CardSelectorKind.Category => card.Categories.Any(category => category.Identity == selector.CategoryIdentities[0]),
        CardSelectorKind.AnyOf when selector.Children.Count > 0 => selector.Children.Any(child => SelectorMatches(child, card)),
        CardSelectorKind.AnyOf => selector.CategoryIdentities.Any(identity => card.Categories.Any(category => category.Identity == identity)),
        CardSelectorKind.AllOf when selector.Children.Count > 0 => selector.Children.All(child => SelectorMatches(child, card)),
        CardSelectorKind.AllOf => selector.CategoryIdentities.All(identity => card.Categories.Any(category => category.Identity == identity)),
        _ => false
    };

    private static void AssertFraction(ProbabilityFraction actual, BigInteger wins, BigInteger outcomes, string description) =>
        Assert.That(BigInteger.Parse(actual.Numerator) * outcomes,
            Is.EqualTo(wins * BigInteger.Parse(actual.Denominator)), description);

    private static void AssertTypedLimit(CalculationSnapshot snapshot, CalculationModel mode,
        ProbabilityCalculationLimitReason reason, CalculationCompletion completion) {
        string json = CalculationWire.Execute(snapshot.Json);
        CalculationResponse response = JsonSerializer.Deserialize(json, CalculationJsonContext.Default.CalculationResponse)!;
        Assert.That(response.Result, Is.Null, mode + " " + reason + " result");
        Assert.That(response.LimitReason, Is.EqualTo(reason), mode + " " + reason + " kind");
        Assert.That(response.FailureKind, Is.Null, mode + " " + reason + " is typed as a resource limit");
        Assert.That(response.Contract!.RequestedMode, Is.EqualTo(mode));
        Assert.That(response.Contract.ExecutedMode, Is.EqualTo(mode));
        Assert.That(response.Contract.Completion, Is.EqualTo(completion));
        Assert.That(response.Contract.Accuracy, Is.Null);
        Assert.That(response.Contract.Fractions, Is.Null);
        Assert.Throws<ProbabilityCalculationLimitException>(() => CalculationWire.ReadResult(json, mode));
    }
}
