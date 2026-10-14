using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.Session;
using YGOProbabilityCalculatorBlazorTest.Services.Session;

namespace YGOProbabilityCalculatorBlazorTest.Components;

[TestFixture]
public class CalculatorEditorArchetypeTest : CalculatorEditorTestBase {
    [TestCase(false)]
    [TestCase(true)]
    public async Task CompleteExampleSkipsLegacyCallAndCloneForPendingAndUploadedSessions(bool upload) {
        ISessionService codec = new SessionService(context.JSInterop.JSRuntime, new YGOProbabilityCalculatorBlazor.Services.Shared.JsonSerializer());
        string source = await File.ReadAllTextAsync(Path.Combine(NUnit.Framework.TestContext.CurrentContext.TestDirectory, "Fixtures", "example_session_state.json"));
        SessionState example = await codec.LoadSessionAsync(source);
        string original = codec.SerializeSession(example);
        Mock<ISessionService> sessions = new();
        sessions.Setup(service => service.SerializeSession(It.IsAny<SessionState>()))
            .Returns((SessionState session) => codec.SerializeSession(session));
        sessions.Setup(service => service.LoadSessionAsync(It.IsAny<string>()))
            .Returns((string json) => codec.LoadSessionAsync(json));
        context.Services.AddSingleton(sessions.Object);
        Mock<ILegacyCardMetadataEnricher> legacy = new(MockBehavior.Strict);
        context.Services.AddSingleton(legacy.Object);
        Mock<ICardInfoService> info = new();
        info.Setup(service => service.GetCatalogCardsAsync()).ReturnsAsync(CatalogArchetypeReconcilerTest.BundledCatalog());
        context.Services.AddSingleton(info.Object);

        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(upload ? null : example);
        if (upload) {
            cut.FindComponents<InputFile>()[1].UploadFiles(InputFileContent.CreateFromText(source, "example.json"));
        }
        cut.WaitForState(() => cut.FindComponents<CardEditor>().Count == 25);
        Card[] restored = cut.FindComponents<CardEditor>().Select(editor => editor.Instance.Card).ToArray();
        string snapshot = (string)context.JSInterop.Invocations["sessionRecovery.update"].Last().Arguments[2]!;
        SessionState expected = await codec.LoadSessionAsync(original);
        CatalogArchetypeReconciler.Reconcile(expected.Cards, CatalogArchetypeReconcilerTest.BundledCatalog());
        Assert.That(snapshot, Is.EqualTo(codec.SerializeSession(expected)), "compare the entire restored session, including effects, combos, groups and settings");
        Assert.That(codec.SerializeSession(example), Is.EqualTo(original), "the supplied session stays untouched");
        Assert.That(restored.Sum(card => card.Copies), Is.EqualTo(40));
        sessions.Verify(service => service.LoadSessionAsync(It.IsAny<string>()), upload ? Times.Once : Times.Never);
        info.Verify(service => service.GetCatalogCardsAsync(), Times.Once);
        legacy.VerifyNoOtherCalls();
    }

    [Test]
    public void ImportedMetadataAndBlankNamesAlsoSkipLegacyPreparationWhileReconcilingArchetypes() {
        CategoryBase stale = new("Archetype: Lightsworn", CategorySource.Metadata, "archetype:lightsworn");
        CategoryBase spell = new("Spell", CategorySource.Metadata, "kind:spell");
        Card named = new([spell, stale], name: "Solar Recharge", drawCount: 1);
        Card blank = new([], name: " ");
        SessionState source = new() { Cards = [named, blank] };
        Mock<ILegacyCardMetadataEnricher> legacy = new(MockBehavior.Strict);
        context.Services.AddSingleton(legacy.Object);
        Mock<ICardInfoService> info = new();
        info.Setup(service => service.GetCatalogCardsAsync()).ReturnsAsync(CatalogArchetypeReconcilerTest.BundledCatalog());
        context.Services.AddSingleton(info.Object);
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(source);
        Card[] restored = cut.FindComponents<CardEditor>().Select(editor => editor.Instance.Card).ToArray();
        Assert.That(restored[0].Categories, Is.EqualTo(new[] { spell }));
        Assert.That(restored[0].EffectPlan, Is.EqualTo(named.EffectPlan));
        Assert.That(restored[1], Is.SameAs(blank));
        Assert.That(source.Cards[0], Is.SameAs(named));
        legacy.VerifyNoOtherCalls();
    }

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
