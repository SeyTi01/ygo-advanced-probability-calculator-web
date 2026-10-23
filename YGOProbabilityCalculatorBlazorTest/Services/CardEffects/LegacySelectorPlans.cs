using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazorTest.Services.CardEffects;

// Frozen shipped plans from dev 4587010, independent of the maintained definitions.
// Old sessions keep these literal predicates, including incomplete/contradictory metadata behavior.
internal static class LegacySelectorPlans {
    internal static readonly int[] Ids = [32807846, 85106525, 57103969, 72323266, 96729612, 67115133, 38120068, 48805472, 26845680, 60764609];

    internal static int Revision(int id) => id switch {
        32807846 or 85106525 or 57103969 or 72323266 or 96729612 => 3,
        67115133 or 48805472 => 2,
        _ => 1
    };

    internal static DrawEffectPlan Plan(int id) {
        CardSelector Range(int maximum) => CardSelector.AnyOf(Enumerable.Range(1, maximum).Select(level => $"metadata:level:{level}"));
        CardSelector Property(string key) => new(CardSelectorKind.Category, ["metadata:" + key]);
        CardSelector Bounded(string key, int maximum) => CardSelector.AllOfSelectors([Property(key), Range(maximum)]);
        CardSelector monster = CardSelector.AllOfSelectors([Property("archetype:radiant%20typhoon"), Range(4)]);
        CardSelector typhoon = CardSelector.CatalogIdentity(5318639);
        return id switch {
            32807846 => new(null, false, 0, CardSelector.Any, 0, 0, CardSelector.Any, Search: new(Bounded("monster-race:warrior", 4))),
            85106525 => new(null, true, 0, CardSelector.Any, 0, 0, CardSelector.Any, Search: new(Bounded("monster-race:pyro", 4))),
            57103969 => new(null, true, 0, CardSelector.Any, 0, 0, CardSelector.Any, Search: new(Bounded("monster-race:beast-warrior", 4))),
            72323266 => new(null, true, 0, CardSelector.Any, 0, 0, CardSelector.Any, Search: new(CardSelector.AllOfSelectors([
                CardSelector.AllOf(["metadata:monster-trait:tuner", "metadata:monster-race:fiend"]), Range(3)
            ]))),
            96729612 => new(null, false, 0, CardSelector.Any, 0, 0, CardSelector.Any, Search: new(Bounded("monster-type:ritual", 7))),
            67115133 => new(null, false, 0, CardSelector.Any, 0, 0, CardSelector.Any,
                Search: new(CardSelector.AnyOfSelectors([monster, typhoon]), true, new SearchEffectMode[] { new(monster, 1), new(typhoon, 1) })),
            38120068 => DrawEffectPlan.Draw(2, false) with { HandCost = 1, HandCostSelector = Property("level:8") },
            48805472 => DrawEffectPlan.Draw(2, true) with {
                HandCost = 1, HandCostSelector = CardSelector.Objective(CardSelector.AnyOfSelectors([
                    CardSelector.AllOfSelectors([Property("level:4"), CardSelector.AnyOf(["metadata:monster-race:insect", "metadata:monster-race:plant"])]),
                    CardSelector.AllOf(["metadata:kind:trap", "metadata:trap-type:normal"])
                ]))
            },
            26845680 => DrawEffectPlan.Draw(2, true) with {
                HandCost = 1, HandCostSelector = CardSelector.AnyOfSelectors([
                    CardSelector.AllOf(["metadata:kind:monster", "metadata:archetype:orcust"]),
                    CardSelector.AllOf(["metadata:kind:monster", "metadata:archetype:world%20legacy"])
                ])
            },
            60764609 => new(null, true, 0, CardSelector.Any, 0, 0, CardSelector.Any, Search: new(CardSelector.AnyOfSelectors([
                CardSelector.AllOf(["metadata:archetype:fiendsmith", "metadata:kind:spell"]),
                CardSelector.AllOf(["metadata:archetype:fiendsmith", "metadata:kind:trap"])
            ]))),
            _ => throw new ArgumentOutOfRangeException(nameof(id))
        };
    }
}
