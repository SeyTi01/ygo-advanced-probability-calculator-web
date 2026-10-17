namespace YGOProbabilityCalculatorBlazor.Models;

public class Combo {
    public List<ComboCategory> Categories { get; }
    public List<ComboCard> Cards { get; }
    public List<ComboAlternativeGroup> AlternativeGroups { get; }
    public string? Name { get; }
    public bool Active { get; }
    public string? GroupId { get; set; }
    private readonly IReadOnlyList<ComboRequirementPosition> _requirementOrder;
    private readonly ComboCategory[] _authoredCategories;
    private readonly ComboCard[] _authoredCards;
    private readonly ComboAlternativeGroup[] _authoredGroups;
    public IReadOnlyList<ComboRequirementPosition> RequirementOrder {
        get {
            if (_authoredCategories.Length == Categories.Count && _authoredCards.Length == Cards.Count && _authoredGroups.Length == AlternativeGroups.Count) {
                return _requirementOrder;
            }
            // Public legacy lists remain mutable. Reconcile external mutations
            // before rendering or saving; immutable editor edits use Copy below.
            ComboRequirementPosition[] current = DefaultOrder().ToArray();
            Dictionary<ComboRequirementPosition, ComboRequirementPosition> mapping = [];
            Map(_authoredCategories, Categories, ComboRequirementKind.Category, mapping);
            Map(_authoredCards, Cards, ComboRequirementKind.Card, mapping);
            Map(_authoredGroups, AlternativeGroups, ComboRequirementKind.Group, mapping);
            ComboRequirementPosition[] retained = _requirementOrder.Where(mapping.ContainsKey).Select(item => mapping[item]).ToArray();
            return Array.AsReadOnly(retained.Concat(current.Except(retained)).ToArray());
        }
    }

    public Combo(
        IEnumerable<ComboCategory> categories,
        string? name = null,
        bool active = true,
        string? groupId = null,
        IEnumerable<ComboCard>? cards = null,
        IEnumerable<ComboAlternativeGroup>? alternativeGroups = null,
        IEnumerable<ComboRequirementPosition>? requirementOrder = null
    ) {
        Categories = [.. categories];
        Cards = cards?.ToList() ?? [];
        AlternativeGroups = alternativeGroups?.ToList() ?? [];
        _authoredCategories = Categories.ToArray();
        _authoredCards = Cards.ToArray();
        _authoredGroups = AlternativeGroups.ToArray();
        Name = name;
        Active = active;
        GroupId = groupId;
        ComboRequirementPosition[] defaults = DefaultOrder().ToArray();
        ComboRequirementPosition[] order = requirementOrder?.ToArray() ?? defaults;
        if (order.Length != defaults.Length || order.Distinct().Count() != order.Length || order.Except(defaults).Any()) {
            throw new ArgumentException("Presentation order must contain each requirement exactly once.", nameof(requirementOrder));
        }
        _requirementOrder = Array.AsReadOnly(order);
    }

    public IEnumerable<ComboCategory> AllCategories => Categories.Concat(
        AlternativeGroups.SelectMany(static group => group.Alternatives)
            .Where(static alternative => alternative.Category is not null).Select(static alternative => alternative.Category!)
    );

    public IEnumerable<ComboCard> AllCards => Cards.Concat(
        AlternativeGroups.SelectMany(static group => group.Alternatives)
            .Where(static alternative => alternative.Card is not null).Select(static alternative => alternative.Card!)
    );

    public int RequirementCount => Categories.Count + Cards.Count + AlternativeGroups.Count;

    public Combo WithName(string? name) => new(Categories, name, Active, GroupId, Cards, AlternativeGroups, RequirementOrder);
    public Combo WithActive(bool active) => new(Categories, Name, active, GroupId, Cards, AlternativeGroups, RequirementOrder);
    public Combo WithGroup(string? groupId) => new(Categories, Name, Active, groupId, Cards, AlternativeGroups, RequirementOrder);
    public Combo WithCategories(IEnumerable<ComboCategory> categories) => Copy(categories.ToList(), Cards, AlternativeGroups);
    public Combo WithCards(IEnumerable<ComboCard> cards) => Copy(Categories, cards.ToList(), AlternativeGroups);
    public Combo WithAlternativeGroups(IEnumerable<ComboAlternativeGroup> groups) => Copy(Categories, Cards, groups.ToList());

    public Combo WithRequirementAt(ComboRequirementPosition requirement, int position) {
        List<ComboRequirementPosition> order = RequirementOrder.Where(item => item != requirement).ToList();
        order.Insert(Math.Clamp(position, 0, order.Count), requirement);
        return new(Categories, Name, Active, GroupId, Cards, AlternativeGroups, order);
    }

    public Combo WithoutRequirement(ComboRequirementPosition removed) {
        if (!RequirementOrder.Contains(removed)) {
            throw new ArgumentOutOfRangeException(nameof(removed));
        }
        ComboRequirementPosition[] order = RequirementOrder.Where(position => position != removed)
            .Select(position => position.Kind == removed.Kind && position.Index > removed.Index ? position with { Index = position.Index - 1 } : position).ToArray();
        return new(
            Categories.Where((category, index) => removed.Kind != ComboRequirementKind.Category || removed.Index != index),
            Name,
            Active,
            GroupId,
            Cards.Where((card, index) => removed.Kind != ComboRequirementKind.Card || removed.Index != index),
            AlternativeGroups.Where((group, index) => removed.Kind != ComboRequirementKind.Group || removed.Index != index),
            order
        );
    }

    private IEnumerable<ComboRequirementPosition> DefaultOrder() =>
        Enumerable.Range(0, Categories.Count).Select(index => new ComboRequirementPosition(ComboRequirementKind.Category, index))
            .Concat(Enumerable.Range(0, Cards.Count).Select(index => new ComboRequirementPosition(ComboRequirementKind.Card, index)))
            .Concat(Enumerable.Range(0, AlternativeGroups.Count).Select(index => new ComboRequirementPosition(ComboRequirementKind.Group, index)));

    private Combo Copy(List<ComboCategory> categories, List<ComboCard> cards, List<ComboAlternativeGroup> groups) {
        Dictionary<ComboRequirementPosition, ComboRequirementPosition> mapping = [];
        Map(Categories, categories, ComboRequirementKind.Category, mapping);
        Map(Cards, cards, ComboRequirementKind.Card, mapping);
        Map(AlternativeGroups, groups, ComboRequirementKind.Group, mapping);
        ComboRequirementPosition[] retained = RequirementOrder.Where(mapping.ContainsKey).Select(item => mapping[item]).ToArray();
        Combo unordered = new(categories, Name, Active, GroupId, cards, groups);
        return new(categories, Name, Active, GroupId, cards, groups, retained.Concat(unordered.RequirementOrder.Except(retained)));
    }

    private static void Map<T>(
        IReadOnlyList<T> previous,
        IReadOnlyList<T> current,
        ComboRequirementKind kind,
        Dictionary<ComboRequirementPosition, ComboRequirementPosition> mapping
    ) where T : class {
        if (previous.Count == current.Count) {
            for (int index = 0; index < previous.Count; index++) {
                mapping[new(kind, index)] = new(kind, index);
            }
            return;
        }
        HashSet<int> used = [];
        for (int index = 0; index < previous.Count; index++) {
            int match = Enumerable.Range(0, current.Count).FirstOrDefault(candidate => !used.Contains(candidate) && ReferenceEquals(previous[index], current[candidate]), -1);
            if (match >= 0) {
                used.Add(match);
                mapping[new(kind, index)] = new(kind, match);
            }
        }
        // Ambiguous removals of the same object use WithoutRequirement, which
        // names the exact occurrence. Other removals retain surviving references.
    }
}
