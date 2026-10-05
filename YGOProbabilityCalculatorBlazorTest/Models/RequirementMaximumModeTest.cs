using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazorTest.Models;

[TestFixture]
public class RequirementMaximumModeTest
{
    [TestCase(0, 0)]
    [TestCase(1, 5)]
    [TestCase(1, 99)]
    public void ThreeArgumentConstructionAlwaysRemainsFixed(int minimum, int maximum)
    {
        ComboCategory category = new(new("Starter"), minimum, maximum);
        ComboCard card = new("starter", minimum, maximum);
        Assert.That(category.MaximumMode, Is.EqualTo(RequirementMaximumMode.Fixed));
        Assert.That(card.MaximumMode, Is.EqualTo(RequirementMaximumMode.Fixed));

        foreach (int handSize in new[] { 1, 5, 6 })
        {
            Assert.That(category.GetEffectiveMaximum(handSize), Is.EqualTo(maximum));
            Assert.That(card.GetEffectiveMaximum(handSize), Is.EqualTo(maximum));
        }

        Assert.That(category.MaxCount, Is.EqualTo(maximum));
        Assert.That(card.MaxCount, Is.EqualTo(maximum));
    }

    [Test]
    public void InvalidModesAndNegativeDynamicBoundsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ComboCategory(new("Starter"), 0, 0, (RequirementMaximumMode)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ComboCard("starter", 0, 0, (RequirementMaximumMode)99));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ComboCategory(new("Starter"), 0, -1, RequirementMaximumMode.HandSize));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ComboCard("starter", 0, -1, RequirementMaximumMode.HandSize));
    }
}
