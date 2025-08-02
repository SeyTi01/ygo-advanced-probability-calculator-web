using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services;

public static class CategoryMerger {
    public static IEnumerable<Category> MergeComboCategories(IEnumerable<Combo> combos, int mask) {
        var comboList = combos.ToList();
        var combined = new List<Category>();

        for (var i = 0; i < comboList.Count; i++) {
            if ((mask & (1 << i)) != 0)
                combined.AddRange(comboList[i].Categories);
        }

        return combined
            .GroupBy(c => c.Name)
            .Select(MergeCategories);
    }

    private static Category MergeCategories(IEnumerable<Category> categories) {
        var categoryList = categories.ToList();
        if (categoryList.Count == 0)
            throw new ArgumentException("Cannot merge empty category list");

        var name = categoryList.First().Name;
        if (categoryList.Any(c => c.Name != name))
            throw new ArgumentException("All categories must have the same name");

        return new Category(
            name,
            categoryList.Max(c => c.MinCount),
            categoryList.Min(c => c.MaxCount)
        );
    }
}
