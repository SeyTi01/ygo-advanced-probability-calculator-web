using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public class CategoryMergerTest {
    [Test]
    public void MergeCombos_WithOverlappingCategories_MergesCorrectly() {
        var catA = new CategoryBase("A");
        var catB = new CategoryBase("B");
        var catC = new CategoryBase("C");

        var combo1 = new Combo([
            new ComboCategory(catA, 1, 4),
            new ComboCategory(catB, 2, 3)
        ]);

        var combo2 = new Combo([
            new ComboCategory(catA, 2, 5),
            new ComboCategory(catC, 1, 2)
        ]);

        var result = CategoryMerger.MergeComboCategories([combo1, combo2], 0b11).ToList();

        Assert.Multiple(() => {
            Assert.That(result, Has.Count.EqualTo(3));

            var categoryA = result.Single(c => c.Name == "A");
            Assert.That(categoryA.MinCount, Is.EqualTo(2));
            Assert.That(categoryA.MaxCount, Is.EqualTo(4));

            var categoryB = result.Single(c => c.Name == "B");
            Assert.That(categoryB.MinCount, Is.EqualTo(2));
            Assert.That(categoryB.MaxCount, Is.EqualTo(3));

            var categoryC = result.Single(c => c.Name == "C");
            Assert.That(categoryC.MinCount, Is.EqualTo(1));
            Assert.That(categoryC.MaxCount, Is.EqualTo(2));
        });
    }

    [Test]
    public void MergeCombos_WithMask_OnlyMergesSelectedCombos() {
        var catA = new CategoryBase("A");

        var combo1 = new Combo([new ComboCategory(catA, 1, 4)]);
        var combo2 = new Combo([new ComboCategory(catA, 2, 5)]);
        var combo3 = new Combo([new ComboCategory(catA, 3, 6)]);

        var result = CategoryMerger.MergeComboCategories([combo1, combo2, combo3], 0b101)
            .Single();

        Assert.Multiple(() => {
            Assert.That(result.Name, Is.EqualTo("A"));
            Assert.That(result.MinCount, Is.EqualTo(3));
            Assert.That(result.MaxCount, Is.EqualTo(4));
        });
    }
}
