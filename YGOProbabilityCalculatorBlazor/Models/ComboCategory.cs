using System.Text.Json.Serialization;
using YGOProbabilityCalculatorBlazor.Services.Converter;

namespace YGOProbabilityCalculatorBlazor.Models;

[JsonConverter(typeof(ComboCategoryConverter))]
public class ComboCategory {
    // Compatibility accessor for atomic requirements. Composite code uses CategoryBases/Selector.
    public CategoryBase BaseCategory => CategoryBases[0];
    public IReadOnlyList<CategoryBase> CategoryBases { get; }
    public CardSelector Selector { get; }
    public string DisplayName => CategoryBases.Count == 1 ? BaseCategory.Name
        : "(" + string.Join(" AND ", CategoryBases.Select(category => category.Name)) + ")";

    public int MinCount { get; }
    public int MaxCount { get; }
    public RequirementMaximumMode MaximumMode { get; }

    public ComboCategory RenameCategory(CategoryBase previous, CategoryBase replacement) => new(
        CategoryBases.Select(category => category.Identity == previous.Identity ? replacement : category),
        MinCount,
        MaxCount,
        MaximumMode
    );

    public int GetEffectiveMaximum(int handSize) => MaximumMode == RequirementMaximumMode.HandSize ? handSize : MaxCount;

    public ComboCategory(
        CategoryBase baseCategory,
        int minCount,
        int maxCount,
        RequirementMaximumMode maximumMode = RequirementMaximumMode.Fixed
    ) : this([baseCategory], minCount, maxCount, maximumMode) { }

    public static ComboCategory SameCard(
        IEnumerable<CategoryBase> categories,
        int minCount,
        int maxCount,
        RequirementMaximumMode maximumMode = RequirementMaximumMode.Fixed
    ) => new(categories, minCount, maxCount, maximumMode);

    private ComboCategory(
        IEnumerable<CategoryBase> categoryBases,
        int minCount,
        int maxCount,
        RequirementMaximumMode maximumMode = RequirementMaximumMode.Fixed
    ) {
        ArgumentNullException.ThrowIfNull(categoryBases);
        CategoryBase[] categories = categoryBases.ToArray();
        if (categories.Length == 0 || categories.Any(category => category is null)) {
            throw new ArgumentException("A category requirement needs at least one category.", nameof(categoryBases));
        }
        CategoryBases = Array.AsReadOnly(categories.DistinctBy(category => category.Identity).ToArray());
        Selector = CardSelector.AllOf(CategoryBases.Select(category => category.Identity));
        if (!Enum.IsDefined(maximumMode)) {
            throw new ArgumentOutOfRangeException(nameof(maximumMode));
        }

        if (minCount < 0) {
            throw new ArgumentOutOfRangeException(nameof(minCount), "Minimum count cannot be negative.");
        }

        if (maxCount < 0) {
            throw new ArgumentOutOfRangeException(nameof(maxCount), "Maximum count cannot be negative.");
        }

        if (maximumMode == RequirementMaximumMode.Fixed && maxCount < minCount) {
            throw new ArgumentOutOfRangeException(nameof(maxCount), "Maximum count cannot be less than minimum count.");
        }

        MinCount = minCount;
        MaxCount = maxCount;
        MaximumMode = maximumMode;
    }
}
