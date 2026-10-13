using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazorTest.Services.Session;

namespace YGOProbabilityCalculatorBlazorTest.Components;

[TestFixture]
public class CalculatorEditorArchetypeTest : CalculatorEditorTestBase {
    [Test]
    public async Task RestoringOldSessionRepairsAutomaticMembershipAndKeepsManualBadgeAndSavedSimulation() {
        Mock<ICardInfoService> service = new();
        service.Setup(info => info.GetCatalogCardsAsync()).ReturnsAsync(CatalogArchetypeReconcilerTest.BundledCatalog());
        context.Services.AddSingleton(service.Object);
        CategoryBase lightsworn = new("Archetype: Lightsworn", CategorySource.Metadata, "archetype:lightsworn");
        Card automatic = new([lightsworn], name: "Solar Recharge", id: "automatic", externalCardId: 691925, drawCount: 1);
        Card manual = new([lightsworn], name: "Manual Solar", id: "manual", externalCardId: 691925,
            manualMetadataCategoryKeys: [lightsworn.MetadataKey!]);
        SessionState old = new() {
            Cards = [automatic, manual], HandSize = 1, DrawEffectsEnabled = false,
            Combos = [new([new(lightsworn, 1, 1)], "Lightsworn")]
        };
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(old);
        Card[] restored = cut.FindComponents<CardEditor>().Select(editor => editor.Instance.Card).ToArray();
        Assert.That(restored[0].Categories, Is.Empty);
        Assert.That(restored[0].EffectPlan, Is.EqualTo(automatic.EffectPlan));
        Assert.That(restored[0].EffectProfile, Is.EqualTo(automatic.EffectProfile));
        Assert.That(restored[1].ManualMetadataCategoryKeys, Does.Contain(lightsworn.MetadataKey));
        Assert.That(cut.Markup, Does.Contain("Archetype: Lightsworn"));
        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(cut.Find(".probability-total-value").TextContent, Is.EqualTo((0.5).ToString("P2")));
        service.Verify(info => info.GetCardInfoByExactNamesAsync(It.IsAny<IEnumerable<string>>()), Times.Never);
    }
}
