namespace YGOProbabilityCalculatorBlazor.Models;

public class Combo(
    IEnumerable<ComboCategory> categories,
    string? name = null,
    bool active = true,
    string? groupId = null,
    IEnumerable<ComboCard>? cards = null,
    IEnumerable<ComboAlternativeGroup>? alternativeGroups = null
)
{
    public List<ComboCategory> Categories { get; } = [.. categories];
    public List<ComboCard> Cards { get; } = cards?.ToList() ?? [];
    public List<ComboAlternativeGroup> AlternativeGroups { get; } =
        alternativeGroups?.ToList() ?? [];

    public IEnumerable<ComboCategory> AllCategories =>
        Categories.Concat(
            AlternativeGroups
                .SelectMany(g => g.Alternatives)
                .Where(a => a.Category is not null)
                .Select(a => a.Category!)
        );

    public IEnumerable<ComboCard> AllCards =>
        Cards.Concat(
            AlternativeGroups
                .SelectMany(g => g.Alternatives)
                .Where(a => a.Card is not null)
                .Select(a => a.Card!)
        );

    public int RequirementCount => Categories.Count + Cards.Count + AlternativeGroups.Count;

    public Combo WithAlternativeGroups(IEnumerable<ComboAlternativeGroup> groups) =>
        new(Categories, Name, Active, GroupId, Cards, groups);

    public string? Name { get; } = name;
    public bool Active { get; } = active;
    public string? GroupId { get; set; } = groupId;

    public Combo WithName(string? name) =>
        new(Categories, name, Active, GroupId, Cards, AlternativeGroups);

    public Combo WithActive(bool active) =>
        new(Categories, Name, active, GroupId, Cards, AlternativeGroups);

    public Combo WithGroup(string? groupId) =>
        new(Categories, Name, Active, groupId, Cards, AlternativeGroups);

    public Combo WithCategories(IEnumerable<ComboCategory> categories)
    {
        ArgumentNullException.ThrowIfNull(categories);

        return new Combo(categories, Name, Active, GroupId, Cards, AlternativeGroups);
    }

    public Combo WithCards(IEnumerable<ComboCard> cards) =>
        new(Categories, Name, Active, GroupId, cards, AlternativeGroups);
}
