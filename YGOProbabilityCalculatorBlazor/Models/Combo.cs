namespace YGOProbabilityCalculatorBlazor.Models;

public class Combo(IEnumerable<ComboCategory> categories, string? name = null, bool active = true, string? groupId = null, IEnumerable<ComboCard>? cards = null) {
    public List<ComboCategory> Categories { get; } = categories.ToList();
    public List<ComboCard> Cards { get; } = cards?.ToList() ?? [];
    public string? Name { get; } = name;
    public bool Active { get; } = active;
    public string? GroupId { get; set; } = groupId;

    public Combo WithName(string? name) => new(Categories, name, Active, GroupId, Cards);
    public Combo WithActive(bool active) => new(Categories, Name, active, GroupId, Cards);
    public Combo WithGroup(string? groupId) => new(Categories, Name, Active, groupId, Cards);

    public Combo WithCategories(IEnumerable<ComboCategory> categories) {
        ArgumentNullException.ThrowIfNull(categories);

        return new Combo(categories, Name, Active, GroupId, Cards);
    }
    public Combo WithCards(IEnumerable<ComboCard> cards) => new(Categories, Name, Active, GroupId, cards);
}
