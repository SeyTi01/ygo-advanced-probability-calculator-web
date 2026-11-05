using CardProbability;

namespace CardProbability.EngineTests.Fixtures;

internal static class SearchCertificateFixtures {
    private static readonly string[] Groups = ["g1", "g2"];

    internal static IEnumerable<EngineCase> Fixtures() {
        foreach (bool once in new[] { false, true }) {
            foreach (bool searchableDraw in new[] { false, true }) {
                foreach (int hidden in new[] { 0, 1, 2 }) {
                    DeckEntry[] entries = [
                        Source("s", "a", once),
                        Source("z", "a-or-b", false),
                        new("t", 2),
                        new("f", 1),
                        new("d", 2, new(DrawCount: 1, ActivationLimit: once ? 1 : null, HiddenDeckCost: hidden > 0 ? 1 : 0)),
                        new("e", 1, new(DrawCount: 1, HiddenPostDrawMill: hidden > 1 ? 1 : 0))
                    ];
                    Membership[] roles = Roles(searchableDraw);
                    yield return Create($"known-source-opt-{once}-searchable-{searchableDraw}-hidden-{hidden}", entries, roles, 2, ["e", "d"]);
                }
            }
        }
        foreach (EngineCase fixture in OrderedCoverage()) {
            yield return fixture;
        }
    }

    internal static IEnumerable<EngineCase> OrderedCoverage() {
        for (int variant = 0; variant < 24; variant++) {
            Eligibility filter = (variant % 3) switch {
                0 => new("material"),
                1 => new("a"),
                _ => Eligibility.Any
            };
            bool preCost = variant >= 12;
            DeckEntry[] entries = [
                Source("s", "a", variant % 4 == 0, 1 + variant % 2),
                new("t", 1),
                new("f", 1),
                new("d", 1, new(
                    DrawCount: 1,
                    HandCost: preCost ? 1 : 0,
                    HandCostEligibility: preCost ? filter : null,
                    RemovalCount: preCost ? 0 : 1,
                    RemovalEligibility: preCost ? null : filter
                )),
                new("e", 1, new(DrawCount: 1, BlocksLaterDraws: variant % 2 == 0)),
                Source("z", "a-or-b", false)
            ];
            if (variant % 2 != 0) {
                Array.Reverse(entries);
            }
            yield return Create($"ordered-proof-cross-product-{variant}", entries, Roles(variant % 4 == 0),
                1 + variant % 3, variant % 2 == 0 ? ["e", "d"] : ["d", "e"]);
        }
    }

    private static DeckEntry Source(string id, string target, bool once, int copies = 1) =>
        new(id, copies, new(ActivationLimit: once ? 1 : null, Search: new(new(target))));

    private static Membership[] Roles(bool drawInA) => [
        new("a", drawInA ? ["t", "d"] : ["t"]),
        new("b", ["t", "e"]),
        new("both", ["t"]),
        new("a-or-b", drawInA ? ["t", "d", "e"] : ["t", "e"]),
        new("material", ["f"])
    ];

    private static EngineCase Create(string name, DeckEntry[] entries, Membership[] roles, int handSize, string[] priority) {
        Goal[] goals = [
            new("a", [Requirement.InMembership("a", 1, 6)], groupId: "g1"),
            new("distinct", [Requirement.InMembership("a", 1, 6), Requirement.InMembership("b", 1, 6)], groupId: "g1"),
            new("same", [Requirement.InMembership("both", 1, 6)], groupId: "g2"),
            new("exclude", [Requirement.InMembership("a", 0, 0)], groupId: "g1"),
            new("exactly", [Requirement.InMembership("a", 1, 1)], groupId: "g2"),
            new("direct", [Requirement.OfEntry("t", 1, 6)], groupId: "g2"),
            new("alternative", [], [new([Requirement.InMembership("a", 1, 6), Requirement.OfEntry("f", 1, 6)])], "g2")
        ];
        return new(name, new(entries, handSize, goals, roles, Groups), new(activationOrder: ActivationOrder.Priority, priorityEntryIds: priority));
    }
}
