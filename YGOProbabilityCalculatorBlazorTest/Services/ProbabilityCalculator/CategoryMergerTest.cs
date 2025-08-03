using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;

[TestFixture]
public class CategoryMergerTest {
    private CategoryBase _monsterCat;
    private CategoryBase _spellCat;
    private CategoryBase _dragonCat;
    private CategoryBase _warriorCat;

    [SetUp]
    public void Setup() {
        _monsterCat = new CategoryBase("Monster");
        _spellCat = new CategoryBase("Spell");
        _dragonCat = new CategoryBase("Dragon");
        _warriorCat = new CategoryBase("Warrior");
    }

    [Test]
    public void MergeComboCategories_SingleCombo_ReturnsSameCategories() {
        var combo = new Combo([
            new ComboCategory(_monsterCat, 2, 4),
            new ComboCategory(_dragonCat, 1, 3)
        ]);

        var result = CategoryMerger.MergeComboCategories([combo], 0b1).ToList();

        Assert.Multiple(() => {
            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result.First(c => c.Name == "Monster").MinCount, Is.EqualTo(2));
            Assert.That(result.First(c => c.Name == "Monster").MaxCount, Is.EqualTo(4));
            Assert.That(result.First(c => c.Name == "Dragon").MinCount, Is.EqualTo(1));
            Assert.That(result.First(c => c.Name == "Dragon").MaxCount, Is.EqualTo(3));
        });
    }

    [Test]
    public void MergeComboCategories_OverlappingCategories_TakesMaxMinAndMinMax() {
        var combo1 = new Combo([new ComboCategory(_monsterCat, 1, 5)]);
        var combo2 = new Combo([new ComboCategory(_monsterCat, 3, 4)]);

        var result = CategoryMerger.MergeComboCategories([combo1, combo2], 0b11)
            .Single();

        Assert.Multiple(() => {
            Assert.That(result.Name, Is.EqualTo("Monster"));
            Assert.That(result.MinCount, Is.EqualTo(3), "Should take maximum of min counts");
            Assert.That(result.MaxCount, Is.EqualTo(4), "Should take minimum of max counts");
        });
    }

    [Test]
    public void MergeComboCategories_MultipleDisjointCategories_PreservesAllCategories() {
        var combo1 = new Combo([new ComboCategory(_monsterCat, 2, 4)]);
        var combo2 = new Combo([new ComboCategory(_spellCat, 1, 3)]);

        var result = CategoryMerger.MergeComboCategories([combo1, combo2], 0b11).ToList();

        List<string> expected = ["Monster", "Spell"];

        Assert.Multiple(() => {
            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result.Select(c => c.Name), Is.EquivalentTo(expected));
        });
    }

    [Test]
    public void MergeComboCategories_ComplexOverlap_HandlesMultipleCategories() {
        var combo1 = new Combo([
            new ComboCategory(_monsterCat, 2, 4),
            new ComboCategory(_dragonCat, 1, 2)
        ]);
        var combo2 = new Combo([
            new ComboCategory(_monsterCat, 1, 3),
            new ComboCategory(_warriorCat, 1, 2)
        ]);

        var result = CategoryMerger.MergeComboCategories([combo1, combo2], 0b11).ToList();

        Assert.Multiple(() => {
            Assert.That(result, Has.Count.EqualTo(3));

            var monster = result.First(c => c.Name == "Monster");
            Assert.That(monster.MinCount, Is.EqualTo(2));
            Assert.That(monster.MaxCount, Is.EqualTo(3));

            var dragon = result.First(c => c.Name == "Dragon");
            Assert.That(dragon.MinCount, Is.EqualTo(1));
            Assert.That(dragon.MaxCount, Is.EqualTo(2));

            var warrior = result.First(c => c.Name == "Warrior");
            Assert.That(warrior.MinCount, Is.EqualTo(1));
            Assert.That(warrior.MaxCount, Is.EqualTo(2));
        });
    }

    [Test]
    public void MergeComboCategories_DifferentMasks_SelectsCorrectCombos() {
        var combo1 = new Combo([new ComboCategory(_monsterCat, 1, 3)]);
        var combo2 = new Combo([new ComboCategory(_monsterCat, 2, 4)]);
        var combo3 = new Combo([new ComboCategory(_monsterCat, 3, 5)]);

        var combos = new[] { combo1, combo2, combo3 };

        Assert.Multiple(() => {
            var result1 = CategoryMerger.MergeComboCategories(combos, 0b101).Single();
            Assert.That(result1.MinCount, Is.EqualTo(3));
            Assert.That(result1.MaxCount, Is.EqualTo(3));

            var result2 = CategoryMerger.MergeComboCategories(combos, 0b011).Single();
            Assert.That(result2.MinCount, Is.EqualTo(2));
            Assert.That(result2.MaxCount, Is.EqualTo(3));
        });
    }

    [Test]
    public void MergeComboCategories_EmptyCombos_ReturnsEmptyList() {
        var result = CategoryMerger.MergeComboCategories([], 0b1);
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void MergeComboCategories_ZeroMask_ReturnsEmptyList() {
        var combo = new Combo([new ComboCategory(_monsterCat, 1, 2)]);
        var result = CategoryMerger.MergeComboCategories([combo], 0);
        Assert.That(result, Is.Empty);
    }
}
