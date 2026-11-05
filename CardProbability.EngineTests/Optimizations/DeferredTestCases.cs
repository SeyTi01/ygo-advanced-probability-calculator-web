using CardProbability.EngineTests.Fixtures;
using CardProbability.Internal;
using CardProbability.Internal.Counting;
using CardProbability.Internal.DrawResolution;
using CardProbability.Internal.Model;
using CardProbability.Internal.Resources;
using Core = CardProbability.Internal.Contracts;

namespace CardProbability.EngineTests.Optimizations;

internal static class DeferredTestCases {
    internal static Requirement Member(string id, int min = 1, int? max = null) => Requirement.InMembership(id, min, max);
    internal static Requirement Entry(string id, int min = 1, int? max = null) => Requirement.OfEntry(id, min, max);
    internal static Goal Goal(string id, params Requirement[] requirements) => new(id, requirements);
    internal static DeckEntry Draw(
        string id,
        int copies = 1,
        int count = 2,
        int? limit = null,
        int cost = 0,
        string? costSet = null,
        int removal = 0,
        string? removalSet = null,
        int hidden = 0,
        int mill = 0
    ) =>
        new(id, copies, new(DrawCount: count, ActivationLimit: limit, HandCost: cost,
            HandCostEligibility: costSet is null ? null : new(costSet), HiddenDeckCost: hidden,
            RemovalCount: removal, RemovalEligibility: removalSet is null ? null : new(removalSet), HiddenPostDrawMill: mill));

    internal static EngineCase Create(
        string name,
        IEnumerable<DeckEntry> entries,
        int hand,
        IEnumerable<Goal> goals,
        IEnumerable<Membership> memberships,
        IEnumerable<string>? groups = null,
        IEnumerable<string>? priority = null
    ) =>
        new(name, new(entries, hand, goals, memberships, groups), priority is null ? new() : new(activationOrder: ActivationOrder.Priority, priorityEntryIds: priority));

    internal static EngineCase Change(
        EngineCase fixture,
        IEnumerable<DeckEntry>? entries = null,
        int? hand = null,
        IEnumerable<Goal>? goals = null,
        IEnumerable<Membership>? memberships = null,
        IEnumerable<string>? priority = null
    ) =>
        Create(fixture.Name, entries ?? fixture.Request.Entries, hand ?? fixture.Request.HandSize, goals ?? fixture.Request.Goals,
            memberships ?? fixture.Request.Memberships, fixture.Request.Groups, priority);

    internal static DrawActivationPriority Priority(EngineCase fixture) => fixture.Options.ActivationOrder == ActivationOrder.Priority
        ? new(DrawActivationMode.CustomPriority, fixture.Options.PriorityEntryIds) : DrawActivationPriority.Default;

    internal static DrawSequenceOracle.Counts AssertDraw(EngineCase fixture, bool? deferred = null, int? peakStates = null) {
        DrawSequenceOracle.Counts oracle = DrawSequenceOracle.Enumerate(fixture);
        foreach (bool reversed in new[] { false, true }) {
            foreach (bool custom in new[] { false, true }) {
                string[]? priority = null;
                if (fixture.Options.ActivationOrder == ActivationOrder.Priority) {
                    priority = fixture.Options.PriorityEntryIds.ToArray();
                }
                else if (custom) {
                    priority = fixture.Request.Entries.Where(entry => entry.Effect?.DrawCount is not null)
                        .Select(entry => entry.Id).Reverse().ToArray();
                }
                EngineCase ordered = Change(fixture, entries: reversed ? fixture.Request.Entries.Reverse() : fixture.Request.Entries, priority: priority);
                DrawSequenceOracle.Counts expected = priority is null ? oracle : DrawSequenceOracle.Enumerate(ordered);
                foreach (bool defer in new[] { false, true }) {
                    WorkBudget budget = new(Core.CalculationWorkPolicy.Interactive);
                    PreparedCalculation input = ordered.Prepare(budget);
                    PostDrawCalculation calculation = new(input.Cards, fixture.Request.HandSize, budget, Priority(ordered), deferIdentities: defer);
                    AssertRows(calculation.Calculate(input.Combos, input.Groups), expected);
                    if (defer && deferred.HasValue) {
                        Assert.That(calculation.DeferredRows > 0, Is.EqualTo(deferred.Value), fixture.Name);
                    }
                    if (peakStates.HasValue) {
                        Assert.That(calculation.PeakStates, Is.LessThan(peakStates.Value));
                    }
                    PostDrawCalculation total = new(input.Cards, fixture.Request.HandSize, new(Core.CalculationWorkPolicy.Interactive), Priority(ordered), deferIdentities: defer);
                    ExactProbability probability = total.Calculate(input.Combos, input.Groups, totalOnly: true).Total;
                    Assert.That(probability.Numerator * expected.Total, Is.EqualTo(expected.Wins[0] * probability.Denominator));
                }
            }
        }
        return oracle;
    }

    internal static void AssertRows(ExactCalculationResult result, DrawSequenceOracle.Counts oracle) {
        ExactProbability[] rows = result.ToRows();
        Assert.That(rows, Has.Length.EqualTo(oracle.Wins.Length));
        for (int row = 0; row < rows.Length; row++) {
            Assert.That(rows[row].Numerator * oracle.Total, Is.EqualTo(oracle.Wins[row] * rows[row].Denominator), "row " + row);
        }
    }
}
