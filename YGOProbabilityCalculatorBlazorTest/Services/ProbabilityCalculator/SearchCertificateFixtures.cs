using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

internal static class SearchCertificateFixtures {
    private static readonly CategoryBase A = new("A");
    private static readonly CategoryBase B = new("B");
    private static readonly CategoryBase M = new("M");
    private static readonly CategoryBase Meta = new("Special metadata", CategorySource.Metadata, "audit:special");
    private static readonly List<ComboGroup> Groups = [new("g1", "Positive and excluded"), new("g2", "Alternative and direct")];
    private static Card Source(string id, CardSelector? selector = null, int copies = 1, bool opt = false, bool objective = false) =>
        new([], copies, id, id: id, search: new(selector ?? CardSelector.For(A), objective), oncePerTurn: opt);
    public sealed record Fixture(string Name, List<Card> Deck, int Hand, List<Combo> Combos, List<ComboGroup> Groups, string[]? Priority = null);

    private static List<Combo> Combos() => [
        new([new(A, 1, 6)], "A", groupId: "g1"),
        new([new(A, 1, 6), new(B, 1, 6)], "Distinct A and B", groupId: "g1"),
        new([ComboCategory.SameCard([A, B], 1, 6)], "Same-card A AND B", groupId: "g2"),
        new([new(A, 0, 0)], "Exclude A", groupId: "g1"),
        new([new(A, 1, 1)], "Exactly one A", groupId: "g2"),
        new([], "Direct target", groupId: "g2", cards: [new("t", 1, 6)]),
        new([], "A OR direct filler", groupId: "g2", alternativeGroups: [new([ComboAlternative.For(new ComboCategory(A, 1, 6)), ComboAlternative.For(new ComboCard("f", 1, 6))])])];

    internal static IEnumerable<Fixture> Fixtures() {
        yield return new("known-source-two-targets",
            [Source("s", copies: 2), new([A, B], 3, id: "t"), new([], 1, id: "f"),
                new([], 1, id: "d", drawCount: 2, postDrawDeckMillCount: 1)],
            2, [new([new(A, 2, 2)], "Exactly two targets", groupId: "g1"),
                new([new(A, 1, 3), new(B, 1, 3)], "Distinct A and B", groupId: "g2"),
                new([new(A, 0, 0)], "Exclude A", groupId: "g1")], Groups);
        foreach (bool opt in new[] { false, true }) {
            foreach (bool searchableDraw in new[] { false, true }) {
                foreach (int hidden in new[] { 0, 1, 2 }) {
                    // Separate source identities, overlapping searches, duplicate
                    // effects and combined banish/mill. Hidden copies may include
                    // either source; only observed consumption can be subtracted.
                    yield return new($"known-source-opt-{opt}-searchable-{searchableDraw}-hidden-{hidden}",
                        [Source("s", opt: opt), Source("z", CardSelector.AnyOf([A.Identity, B.Identity])),
                            new([A, B], 2, id: "t"), new([], 1, id: "f"),
                            new(searchableDraw ? [A] : [], 2, id: "d", drawCount: 1, drawOncePerTurn: opt,
                                preDrawDeckRemovalCount: hidden > 0 ? 1 : 0),
                            new([B], 1, id: "e", drawCount: 1, postDrawDeckMillCount: hidden > 1 ? 1 : 0)],
                        2, Combos(), Groups, ["e", "d"]);
                }
            }
        }
        foreach (Fixture fixture in OrderedCoverage()) {
            yield return fixture;
        }
        // Structured ordered cross-product: overlapping searches, distinct IDs
        // with duplicate names, OPT copies, custom order, and removable sources.
        for (int variant = 0; variant < 24; variant++) {
            CardSelector filter = variant % 3 == 0 ? CardSelector.For(M)
                : variant % 3 == 1 ? CardSelector.For(A) : CardSelector.Any;
            List<Card> ordered = [Source("s", copies: 1 + variant % 2, opt: variant % 4 == 0),
                new([A, B], 1, "same", id: "t"), new([M], 1, id: "f"),
                new(variant % 4 == 0 ? [A] : [], 1, "same", id: "d", drawCount: 1,
                    postDrawDiscardCount: variant < 12 ? 1 : 0, postDrawSelector: variant < 12 ? filter : null,
                    preDrawRemovalCount: variant >= 12 ? 1 : 0, preDrawSelector: variant >= 12 ? filter : null),
                new([], 1, "same", id: "e", drawCount: 1, blocksLaterDraws: variant % 2 == 0),
                Source("z", CardSelector.AnyOf([A.Identity, B.Identity]))];
            if (variant % 2 == 1) {
                ordered.Reverse();
            }
            yield return new("ordered-proof-cross-product-" + variant, ordered, 1 + variant % 3,
                Combos(), Groups, variant % 2 == 0 ? ["e", "d"] : ["d", "e"]);
        }
        CategoryBase monster = new("Monster", CategorySource.Metadata, "kind:monster");
        CategoryBase level = new("Level 14", CategorySource.Metadata, "level:14");
        CardSelector grouped = CardSelector.AllOfSelectors([CardSelector.LevelAtLeast(5),
            CardSelector.AnyOfSelectors([CardSelector.For(A), CardSelector.For(B)])]);
        foreach (bool hidden in new[] { false, true }) {
            foreach (bool drawIsTarget in new[] { false, true }) {
                List<Card> groupedDeck = [Source("s", grouped), new([A, monster, level], 1, id: "t", externalCardId: 1),
                    new([A, monster, level], 1, id: "manual", externalCardId: 2, manualMetadataCategoryKeys: ["level:14"]),
                    new([B], 1, id: "b"), new([], 2, id: "f"),
                    new(drawIsTarget ? [A, monster, level] : [], 1, id: "d", externalCardId: 3, drawCount: 1, preDrawDeckRemovalCount: hidden ? 1 : 0)];
                yield return new($"grouped-level-hidden-{hidden}-target-source-{drawIsTarget}", groupedDeck, 2, Combos(), Groups);
            }
        }
        yield return new("two-count-state-draws-custom-priority",
            [Source("s"), new([A], 2, id: "t"), new([B], 1, id: "b"), new([], 2, id: "f"),
                new([], 1, id: "d1", drawCount: 1), new([], 1, id: "d2", drawCount: 2)], 2, Combos(), Groups, ["d2", "d1"]);
        List<Card> basic = [Source("s"), new([A, B], 1, "target", id: "t"), new([B], 1, "b", id: "b"), new([], 2, "filler", id: "f")];
        yield return new("search-only", basic, 2, Combos(), Groups);
        yield return new("zero-capacity", basic, 5, Combos(), Groups);
        yield return new("zero-target", [Source("s"), new([], 3, id: "f")], 2, Combos(), Groups);
        foreach (bool opt in new[] { false, true }) {
            yield return new("competing-duplicates-opt-" + opt, [Source("s", copies: 2, opt: opt), Source("z", CardSelector.AnyOf([A.Identity, B.Identity])), new([A, B], 1, id: "t"), new([B], 1, id: "b"), new([], 1, id: "f")], 3, Combos(), Groups);
        }
        yield return new("fetched-searcher-inert", [Source("s", CardSelector.Any), new([A], 1, id: "z", search: new(CardSelector.For(B), false)), new([B], 1, id: "t"), new([], 2, id: "f")], 1, Combos(), Groups);
        yield return new("objective-provenance", [Source("s", CardSelector.For(Meta), objective: true), new([A, Meta], 1, id: "t", externalCardId: 1), new([A, Meta], 1, id: "manual", externalCardId: 2, manualMetadataCategoryKeys: [Meta.MetadataKey!]), new([A, Meta], 1, id: "unidentified"), new([M], 1, id: "f")], 2, Combos(), Groups);
        yield return new("inactive-zero", [.. basic, new([A], 2, active: false, id: "inactive"), new([A], 0, id: "zero")], 2, Combos(), Groups);
        for (int kind = 0; kind < 9; kind++) {
            Card draw = new([], 1, id: "d", drawCount: kind == 0 ? 1 : 2,
                preDrawRemovalCount: kind is 2 or 7 ? 1 : 0,
                preDrawSelector: kind is 2 or 7 ? CardSelector.For(A) : null,
                postDrawDiscardCount: kind is 3 or 4 or 7 ? 1 : 0,
                postDrawSelector: kind == 4 ? CardSelector.For(A) : kind == 7 ? CardSelector.For(M) : null,
                preDrawDeckRemovalCount: kind is 5 or 8 ? 1 : 0,
                postDrawDeckMillCount: kind is 6 or 8 ? 1 : 0);
            List<Card> deck = [.. basic, draw];
            yield return new("draw-operation-" + kind, deck, 2, Combos(), Groups);
            yield return new("draw-operation-row-reversed-" + kind, deck.AsEnumerable().Reverse().ToList(), 3, Combos(), Groups);
        }
        yield return new("two-draws-custom-priority", [Source("s"), new([A], 1, id: "t"), new([B], 1, id: "f"), new([], 1, id: "d1", drawCount: 1), new([], 1, id: "d2", drawCount: 2, postDrawDiscardCount: 1), new([A, B], 1, id: "b")], 3, Combos(), Groups, ["d2", "d1"]);
        yield return new("draw-target-source-consumed", [Source("s", CardSelector.Any), new([A], 1, id: "t", drawCount: 1), new([], 2, id: "f")], 2, Combos(), Groups);
        // Pool members have identical observed masks, uneven copies, and ID-biased removal.
        yield return new("coalesced-uneven-targets", [Source("s"), new([A], 1, id: "aa"), new([A], 2, id: "zz"), new([], 1, id: "d", drawCount: 2, postDrawDiscardCount: 1), new([], 1, id: "f")], 2, [new([new(A, 1, 2)], "A", groupId: "g1")], Groups);
        yield return new("deferred-unobserved-targets", [Source("s"), new([A], 1, id: "t"), new([B], 1, id: "b"), new([M], 1, id: "m"), new([], 1, id: "d", drawCount: 1, postDrawDiscardCount: 1, postDrawSelector: CardSelector.For(M)), new([], 1, id: "f")], 2, Combos(), Groups);
        List<Combo> positive = [new([new(A, 1, 2)], "A", groupId: "g1"), new([new(B, 1, 2)], "B", groupId: "g1"), new([new(A, 1, 2), new(B, 1, 2)], "Distinct A+B", groupId: "g2")];
        yield return new("minimal-filtered-target-discard", [Source("s"), new([A], 1, id: "t"), new([], 2, id: "f"), new([], 1, id: "d", drawCount: 1, postDrawDiscardCount: 1, postDrawSelector: CardSelector.For(A))], 2, positive, Groups);
        yield return new("minimal-hidden-only-target", [Source("s"), new([A], 1, id: "t"), new([], 2, id: "f"), new([], 1, id: "d", drawCount: 1, preDrawDeckRemovalCount: 1)], 2, positive, Groups);
        yield return new("minimal-milled-only-target", [Source("s"), new([A], 1, id: "t"), new([], 2, id: "f"), new([], 1, id: "d", drawCount: 1, postDrawDeckMillCount: 1)], 2, positive, Groups);
        yield return new("shared-budget-union", [Source("s", CardSelector.AnyOf([A.Identity, B.Identity])), new([A], 1, id: "t"), new([B], 1, id: "b"), new([], 1, id: "d", drawCount: 1, preDrawDeckRemovalCount: 1), new([], 2, id: "f")], 2, positive, Groups);
        yield return new("shared-budget-strict-category", [Source("s"), new([A], 1, id: "a1"), new([A], 1, id: "a2"), new([A], 1, id: "a3"), new([], 1, id: "d", drawCount: 1, preDrawDeckRemovalCount: 1), new([], 1, id: "f")], 2, [new([new(A, 1, 2)], "A", groupId: "g1")], Groups);
        yield return new("shared-budget-strict-union", [Source("s", CardSelector.AnyOf([A.Identity, B.Identity, M.Identity])), new([A], 1, id: "a1"), new([B], 1, id: "a2"), new([M], 1, id: "a3"), new([], 1, id: "d", drawCount: 1, preDrawDeckRemovalCount: 1), new([], 1, id: "f")], 2, [new([new(A, 1, 2)], "A", groupId: "g1"), new([new(B, 1, 2)], "B", groupId: "g1"), new([new(M, 1, 2)], "M", groupId: "g1")], Groups);
        yield return new("mixed-visible-hidden", [Source("s"), new([A], 2, id: "t"), new([B], 1, id: "b"), new([], 1, id: "d", drawCount: 1, postDrawDiscardCount: 1, preDrawDeckRemovalCount: 1, postDrawDeckMillCount: 1), new([], 2, id: "f")], 2, positive, Groups);
        yield return new("multiple-draw-opt-duplicates", [Source("s"), new([A], 1, id: "t"), new([B], 1, id: "b"), new([], 2, id: "d", drawCount: 1, drawOncePerTurn: true), new([], 1, id: "z", drawCount: 2, preDrawRemovalCount: 1), new([], 1, id: "f")], 3, positive, Groups, ["z", "d"]);
        yield return new("solar-style-cost-and-mill", [Source("s"), new([A, M], 1, id: "t"), new([A, M], 1, id: "a"), new([B], 1, id: "b"), new([], 2, id: "f"), new([M], 1, id: "m"), new([], 1, id: "d", drawCount: 2, preDrawRemovalCount: 1, preDrawSelector: CardSelector.AllOf([A.Identity, M.Identity]), postDrawDeckMillCount: 2)], 2, positive, Groups);
        yield return new("or-and-filtered-removal", [Source("s"), new([A, M], 1, id: "t"), new([A], 1, id: "a"), new([B], 1, id: "b"), new([], 1, id: "f"), new([], 1, id: "d", drawCount: 2, postDrawDiscardCount: 1, postDrawSelector: CardSelector.AnyOfSelectors([CardSelector.AllOf([A.Identity, M.Identity]), CardSelector.For(B)]))], 2, positive, Groups);
        yield return new("maximum-only-removal", [Source("s"), new([A], 1, id: "t"), new([B], 1, id: "b"), new([], 1, id: "f"), new([], 1, id: "d", drawCount: 1, postDrawDiscardCount: 1)], 2, [new([new(A, 0, 0)], "No A", groupId: "g1"), new([], "No source", groupId: "g2", cards: [new("s", 0, 0)])], Groups);
        // Fixed seed chooses configurations only. Every resulting physical shuffle is enumerated.
        Random random = new(204205);
        for (int test = 0; test < 80; test++) {
            List<Card> deck = [Source("s", test % 3 == 0 ? CardSelector.AnyOf([A.Identity, B.Identity]) : CardSelector.For(A), opt: test % 2 == 0)];
            for (int row = 0; row < 3; row++) {
                List<CategoryBase> categories = [];
                if (random.Next(2) == 1) { categories.Add(A); }
                if (random.Next(2) == 1) { categories.Add(B); }
                if (random.Next(2) == 1) { categories.Add(M); }
                deck.Add(new(categories, 1, id: row == 0 ? "t" : row == 1 ? "f" : "b"));
            }
            int operation = test % 8;
            deck.Add(new([], 1, id: test % 2 == 0 ? "aa-d" : "zz-d", drawCount: 1 + test % 2,
                postDrawDiscardCount: operation is 1 or 2 ? 1 : 0, postDrawSelector: operation == 2 ? CardSelector.For(A) : null,
                preDrawRemovalCount: operation == 3 ? 1 : 0, preDrawSelector: operation == 3 ? CardSelector.For(M) : null,
                preDrawDeckRemovalCount: operation == 4 ? 1 : 0, postDrawDeckMillCount: operation == 5 ? 1 : 0));
            deck.Add(test % 3 == 0 ? Source("z", CardSelector.AllOf([A.Identity, B.Identity])) : new([B], 1, id: "extra"));
            if (test % 2 == 1) { deck.Reverse(); }
            yield return new("seed-204205-" + test, deck, 1 + test % 4, Combos(), Groups);
        }
    }

    internal static IEnumerable<Fixture> OrderedCoverage() {
        foreach (bool reverse in new[] { false, true }) {
            foreach (int variant in Enumerable.Range(0, 4)) {
                List<Card> deck = [Source("s"), new([A], reverse ? 1 : 2, id: "aa"),
                    new(variant == 2 ? [A, B] : [A], reverse ? 2 : 1, id: "zz"),
                    new([], 1, id: "d", drawCount: 1, postDrawDiscardCount: 1,
                        postDrawSelector: CardSelector.For(A), preDrawDeckRemovalCount: variant == 3 ? 1 : 0),
                    variant == 2 ? Source("z", CardSelector.For(B)) : new([], 1, id: "f")];
                List<Combo> combos = [new([new(A, 1, 2)], "A", groupId: "g1"), new([new(A, 0, 0)], "No A", groupId: "g2")];
                if (variant == 1) {
                    combos.Add(new([], "Direct aa", groupId: "g1", cards: [new("aa", 1, 1)]));
                }
                if (reverse) { deck.Reverse(); }
                yield return new($"ordered-pooling-{variant}-reversed-{reverse}", deck, 2, combos, Groups);
            }
        }
        foreach (bool cost in new[] { false, true }) {
            foreach (bool first in new[] { false, true }) {
                List<Card> deck = [Source("s"), new([A], 2, id: "aa"), new([B], 1, id: "b"), new([M], 1, id: "m"),
                    new([], 1, id: "d", drawCount: 1, preDrawRemovalCount: cost ? 1 : 0,
                        preDrawSelector: cost ? CardSelector.For(M) : null, postDrawDiscardCount: cost ? 0 : 1,
                        postDrawSelector: cost ? null : CardSelector.For(M), preDrawDeckRemovalCount: 1,
                        firstActivationOnly: first),
                    new([], 1, id: "e", drawCount: 1)];
                yield return new($"deferred-loss-cost-{cost}-first-{first}", deck, 2, Combos(), Groups, ["d", "e"]);
            }
        }
    }

}
