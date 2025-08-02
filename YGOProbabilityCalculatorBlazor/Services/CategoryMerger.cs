using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services;

public static class CategoryMerger {
    public static IEnumerable<Category> MergeComboCategories(IEnumerable<Combo> combos, int mask) {
        var comboList = combos.ToList();
        var combined = new List<ComboCategory>();

        for (var i = 0; i < comboList.Count; i++) {
            if ((mask & (1 << i)) != 0)
                combined.AddRange(comboList[i].Categories);
        }

        return combined
            .GroupBy(c => c.BaseCategory.Name)
            .Select(MergeCategories);
    }

    private static Category MergeCategories(IGrouping<string, ComboCategory> categories) {
        if (!categories.Any())
            throw new ArgumentException("Cannot merge empty category list");

        var name = categories.Key;
        return new Category(
            name,
            categories.Max(c => c.MinCount),
            categories.Min(c => c.MaxCount)
        );
    }
}
