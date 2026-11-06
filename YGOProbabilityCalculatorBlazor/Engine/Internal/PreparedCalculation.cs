using CardProbability.Internal.Resources;
using M = CardProbability.Internal.Model;

namespace CardProbability.Internal;

// Request-local mutable solver objects never escape the public immutable boundary.
internal sealed record PreparedCalculation(List<M.Card> Cards, List<M.Combo> Combos, List<M.ComboGroup> Groups) {
    internal static PreparedCalculation Create(CalculationRequest request, CalculationOptions options, WorkBudget budget) {
        ArgumentNullException.ThrowIfNull(request);
        HashSet<string> entries = Ids(request.Entries.Select(entry => entry?.Id), "entry");
        HashSet<string> memberships = Ids(request.Memberships.Select(membership => membership?.Id), "membership");
        HashSet<string> goals = Ids(request.Goals.Select(goal => goal?.Id), "goal");
        HashSet<string> groups = Ids(request.Groups, "group");
        long copies = 0;
        bool explicitPolicyOrder = request.Entries.Any(entry => entry.PolicyOrderKey is not null);
        Dictionary<string, List<M.CategoryBase>> categories = entries.ToDictionary(id => id, _ => new List<M.CategoryBase>(), StringComparer.Ordinal);
        Dictionary<string, HashSet<string>> eligible = new(StringComparer.Ordinal);
        Dictionary<string, M.CardSelector> selectors = new(StringComparer.Ordinal);
        long cells = request.Entries.Length + (long)request.Goals.Length + request.Memberships.Length + request.Groups.Length;
        budget.CheckStorage(cells, cells);

        foreach (DeckEntry entry in request.Entries) {
            budget.Spend(1);
            if (entry.Copies < 0) {
                throw new ArgumentException("Entry multiplicities cannot be negative.");
            }
            copies += entry.Copies;
            if (explicitPolicyOrder && entry.PolicyOrderKey is null) {
                throw new ArgumentException("Supply a policy order key for every entry or for none of them.");
            }
            cells += entry.PolicyOrderKey?.Length ?? 0;
            budget.CheckStorage(entries.Count, cells);
        }
        if (copies > int.MaxValue) {
            throw new NotSupportedException("Physical copy totals cannot exceed Int32.MaxValue.");
        }
        if (request.HandSize < 0 || request.HandSize > copies) {
            throw new ArgumentException("The opening hand size must fit the physical deck.");
        }

        foreach (Membership membership in request.Memberships) {
            HashSet<string> members = Ids(membership.EntryIds, "membership entry");
            if (!members.IsSubsetOf(entries)) {
                throw new ArgumentException("A membership references an unknown entry.");
            }
            eligible.Add(membership.Id, members);
            M.CategoryBase category = new(membership.Id);
            selectors.Add(membership.Id, M.CardSelector.Category(membership.Id));
            cells += members.Count;
            budget.CheckStorage(eligible.Count + entries.Count, cells);
            foreach (string id in members) {
                budget.Spend(1);
                categories[id].Add(category);
            }
        }

        HashSet<string> priority = Ids(options.PriorityEntryIds, "priority entry");
        if (!priority.IsSubsetOf(entries)) {
            throw new ArgumentException("Activation priority references an unknown entry.");
        }

        List<M.Card> cards = new(request.Entries.Length);
        foreach (DeckEntry entry in request.Entries) {
            budget.Spend(1);
            ValidateEffect(entry.Effect);
            M.DrawEffectPlan plan = options.Mode == CalculationMode.OpeningHand || entry.Effect is null
                ? new(null, false, 0, M.CardSelector.Any, 0, 0, M.CardSelector.Any)
                : Effect(entry.Effect);
            cards.Add(new(entry.Id, entry.Copies, categories[entry.Id], plan, entry.PolicyOrderKey));
        }

        List<M.Combo> combos = new(request.Goals.Length);
        foreach (Goal goal in request.Goals) {
            budget.Spend(1);
            if (goal.GroupId is not null && !groups.Contains(goal.GroupId)) {
                throw new ArgumentException("A goal references an unknown group.");
            }
            List<M.ComboCategory> requiredMemberships = [];
            List<M.ComboCard> requiredEntries = [];
            foreach (Requirement requirement in goal.Requirements) {
                M.ComboAlternative converted = ConvertRequirement(requirement);
                if (converted.Category is { } category) {
                    requiredMemberships.Add(category);
                }
                else {
                    requiredEntries.Add(converted.Card!);
                }
            }
            List<M.ComboAlternativeGroup> alternatives = new(goal.Alternatives.Length);
            foreach (Alternative alternative in goal.Alternatives) {
                if (alternative is null || alternative.Choices.IsEmpty) {
                    throw new ArgumentException("An alternative group needs at least one requirement.");
                }
                alternatives.Add(new(alternative.Choices.Select(ConvertRequirement).ToArray()));
            }
            combos.Add(new(requiredMemberships, goal.Id, groupId: goal.GroupId, cards: requiredEntries, alternativeGroups: alternatives));
        }

        return new(cards, combos, request.Groups.Select(id => new M.ComboGroup(id, id)).ToList());

        HashSet<string> Ids(IEnumerable<string?> values, string kind) {
            HashSet<string> ids = new(StringComparer.Ordinal);
            foreach (string? id in values) {
                budget.Spend(1);
                if (string.IsNullOrWhiteSpace(id) || !ids.Add(id)) {
                    throw new ArgumentException($"Each {kind} ID must be nonempty and unique.");
                }
                budget.CheckStorage(ids.Count, ids.Count);
            }
            return ids;
        }

        M.CardSelector Filter(Eligibility? filter) {
            if (filter?.MembershipId is null) {
                return M.CardSelector.Any;
            }
            if (!memberships.Contains(filter.MembershipId)) {
                throw new ArgumentException("An effect references an unknown membership.");
            }
            return selectors[filter.MembershipId];
        }

        HashSet<string> Members(Eligibility filter) => filter.MembershipId is null ? entries : eligible[filter.MembershipId];

        void ValidateEffect(EffectPlan? effect) {
            if (effect is null) {
                return;
            }
            if (effect.DrawCount is <= 0 || effect.ActivationLimit is <= 0 || effect.HandCost < 0 || effect.HiddenDeckCost < 0
                || effect.RemovalCount < 0 || effect.HiddenPostDrawMill < 0 || effect.MinimumHandSize < 0) {
                throw new ArgumentException("Operation counts must be nonnegative and draw counts and activation limits must be positive.");
            }
            int operations = (effect.DrawCount.HasValue ? 1 : 0) + (effect.Search is not null ? 1 : 0) + (effect.Excavation is not null ? 1 : 0);
            if (operations != 1) {
                throw new NotSupportedException("Each effect source must contain exactly one draw, search, or excavation operation.");
            }
            Filter(effect.HandCostEligibility);
            Filter(effect.RemovalEligibility);
            if ((effect.HandCost == 0 && effect.HandCostEligibility?.MembershipId is not null)
                || (effect.RemovalCount == 0 && effect.RemovalEligibility?.MembershipId is not null)) {
                throw new ArgumentException("Filtered hand operations require a positive count.");
            }
            if (effect.DrawCount is null && (effect.HiddenDeckCost > 0 || effect.HiddenPostDrawMill > 0 || effect.FirstActivationOnly || effect.BlocksLaterDraws)) {
                throw new NotSupportedException("Hidden deck costs and draw sequencing restrictions require a draw operation.");
            }
            if (effect.Excavation is { } excavation) {
                if (excavation.Count <= 0) {
                    throw new ArgumentException("Excavation count must be positive.");
                }
                if (effect.HandCost != 0 || effect.RemovalCount != 0 || effect.MinimumHandSize != 0 || effect.ActivationLimit != 1) {
                    throw new NotSupportedException("Excavation is standalone and has one activation allowance.");
                }
            }
            if (effect.Search is not { } search) {
                return;
            }
            ArgumentNullException.ThrowIfNull(search.Targets);
            Filter(search.Targets);
            if (!search.Modes.IsEmpty) {
                HashSet<string> union = new(StringComparer.Ordinal);
                foreach (SearchMode mode in search.Modes) {
                    budget.Spend(1);
                    if (mode is null || mode.Targets is null || mode.ActivationLimit is <= 0) {
                        throw new ArgumentException("Search modes require targets and positive activation limits.");
                    }
                    Filter(mode.Targets);
                    union.UnionWith(Members(mode.Targets));
                    budget.CheckStorage(union.Count + search.Modes.Length, union.Count + search.Modes.Length);
                }
                if (!union.SetEquals(Members(search.Targets))) {
                    throw new ArgumentException("Search targets must equal the union of the mode targets.");
                }
            }
        }

        M.DrawEffectPlan Effect(EffectPlan effect) {
            M.SearchEffectPlan? search = null;
            if (effect.Search is { } operation) {
                M.SearchEffectMode[]? modes = operation.Modes.IsEmpty ? null
                    : operation.Modes.Select(mode => new M.SearchEffectMode(Filter(mode.Targets), mode.ActivationLimit)).ToArray();
                M.CardSelector target = modes is null ? Filter(operation.Targets) : M.SearchEffectPlan.ModeTargetUnion(modes);
                search = new(target, modes);
            }
            return new(
                effect.DrawCount,
                effect.ActivationLimit == 1,
                effect.HandCost,
                Filter(effect.HandCostEligibility),
                effect.HiddenDeckCost,
                effect.RemovalCount,
                Filter(effect.RemovalEligibility),
                HiddenPostDrawMill: effect.HiddenPostDrawMill,
                FirstActivationOnly: effect.FirstActivationOnly,
                BlocksLaterDraws: effect.BlocksLaterDraws,
                Search: search,
                ActivationLimit: effect.ActivationLimit,
                MinimumHandSize: effect.MinimumHandSize,
                Excavation: effect.Excavation is { } excavation ? new(excavation.Count, excavation.RequiresNoPriorEffectDraw) : null
            );
        }

        M.ComboAlternative ConvertRequirement(Requirement requirement) {
            budget.Spend(1);
            if (requirement is null || !Enum.IsDefined(requirement.Kind) || requirement.Minimum < 0 || requirement.Maximum is < 0) {
                throw new ArgumentException("A requirement needs a valid target and nonnegative bounds.");
            }
            HashSet<string> targets = requirement.Kind == RequirementTargetKind.Entry ? entries : memberships;
            if (!targets.Contains(requirement.Id)) {
                throw new ArgumentException("A requirement references an unknown target.");
            }
            M.RequirementMaximumMode maximumMode = requirement.Maximum is null ? M.RequirementMaximumMode.HandSize : M.RequirementMaximumMode.Fixed;
            return requirement.Kind == RequirementTargetKind.Entry
                ? M.ComboAlternative.For(new M.ComboCard(requirement.Id, requirement.Minimum, requirement.Maximum ?? 0, maximumMode))
                : M.ComboAlternative.For(new M.ComboCategory(selectors[requirement.Id], requirement.Minimum, requirement.Maximum ?? 0, maximumMode));
        }
    }
}
