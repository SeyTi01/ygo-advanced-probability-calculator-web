using CardProbability;
using CardProbability.Internal;
using CardProbability.Internal.Resources;
using Core = CardProbability.Internal.Contracts;

namespace CardProbability.EngineTests.Fixtures;

internal sealed record EngineCase(string Name, CalculationRequest Request, CalculationOptions Options) {
    internal PreparedCalculation Prepare(WorkBudget? budget = null) => PreparedCalculation.Create(
        Request,
        Options,
        budget ?? new WorkBudget(Core.CalculationWorkPolicy.Unlimited)
    );
}

// This interpretation uses only the authored public inputs, not compiled solver masks.
internal sealed class OracleModel(CalculationRequest request) {
    private readonly Dictionary<string, HashSet<string>> _memberships = request.Memberships.ToDictionary(
        membership => membership.Id,
        membership => membership.EntryIds.ToHashSet(StringComparer.Ordinal),
        StringComparer.Ordinal
    );

    internal bool Eligible(Eligibility? eligibility, DeckEntry entry) => eligibility?.MembershipId is null
        || _memberships[eligibility.MembershipId].Contains(entry.Id);

    internal bool Matches(Requirement requirement, DeckEntry entry) => requirement.Kind == RequirementTargetKind.Entry
        ? requirement.Id == entry.Id
        : _memberships[requirement.Id].Contains(entry.Id);

    internal IEnumerable<Requirement> AllRequirements(Goal goal) => goal.Requirements.Concat(
        goal.Alternatives.SelectMany(group => group.Choices)
    );

    internal bool GoalMatches(IReadOnlyList<DeckEntry> hand, Goal goal) {
        List<Requirement> selected = [.. goal.Requirements];
        return ChooseAlternative(0);

        bool ChooseAlternative(int index) {
            if (index < goal.Alternatives.Length) {
                foreach (Requirement choice in goal.Alternatives[index].Choices) {
                    selected.Add(choice);
                    bool found = ChooseAlternative(index + 1);
                    selected.RemoveAt(selected.Count - 1);
                    if (found) {
                        return true;
                    }
                }
                return false;
            }

            // Identical targets intersect their count bounds rather than
            // consuming another independent set of positive slots.
            List<Requirement> normalized = selected.GroupBy(item => (item.Kind, item.Id)).Select(group => new Requirement(
                group.Key.Kind,
                group.Key.Id,
                group.Max(item => item.Minimum),
                group.Min(item => item.Maximum ?? hand.Count)
            )).ToList();
            List<Requirement> slots = [];
            foreach (Requirement requirement in normalized) {
                int count = hand.Count(entry => Matches(requirement, entry));
                if (count < requirement.Minimum || count > requirement.Maximum) {
                    return false;
                }
                for (int slot = 0; slot < requirement.Minimum; slot++) {
                    slots.Add(requirement);
                }
            }

            bool[] used = new bool[hand.Count];
            return Assign(0);
            bool Assign(int slot) {
                if (slot == slots.Count) {
                    return true;
                }
                for (int index = 0; index < hand.Count; index++) {
                    if (!used[index] && Matches(slots[slot], hand[index])) {
                        used[index] = true;
                        if (Assign(slot + 1)) {
                            return true;
                        }
                        used[index] = false;
                    }
                }
                return false;
            }
        }
    }

    internal bool[] Rows(IReadOnlyList<DeckEntry> hand) {
        bool[] matches = request.Goals.Select(goal => GoalMatches(hand, goal)).ToArray();
        return [matches.Any(value => value), .. matches,
            .. request.Groups.Select(group => request.Goals.Where((goal, index) => goal.GroupId == group && matches[index]).Any())];
    }

    internal string OrderKey(DeckEntry entry) {
        if (entry.PolicyOrderKey is not null) {
            return entry.PolicyOrderKey;
        }
        string[] memberships = request.Goals.SelectMany(AllRequirements).Where(requirement => requirement.Kind == RequirementTargetKind.Membership)
            .Select(requirement => requirement.Id).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        string[] entries = request.Goals.SelectMany(AllRequirements).Where(requirement => requirement.Kind == RequirementTargetKind.Entry)
            .Select(requirement => requirement.Id).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        return string.Concat(memberships.Select(id => _memberships[id].Contains(entry.Id) ? '1' : '0'))
            + string.Concat(entries.Select(id => id == entry.Id ? '1' : '0'));
    }
}
