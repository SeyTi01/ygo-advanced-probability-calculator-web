using Bunit;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Components;

[TestFixture]
public sealed class CalculatorEditorLanguageDraftTest : CalculatorEditorTestBase {
    private IRenderedComponent<ProbabilityCalculatorComponent> RenderDraftWorkspace() => Render(new() {
        Categories = [a], Cards = [new([], 1, "Card", id: "card")], ComboGroups = [new("group", "Group")]
    });

    [Test]
    public void RawInputProtectionIdentifiesUnsubmittedFieldsAndAcceptedNumberControls() {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = RenderDraftWorkspace();

        Assert.That(cut.Find("[data-language-workspace]"), Is.Not.Null);
        Assert.That(cut.Find("#quickCardName").HasAttribute("data-language-draft-input"), Is.True);
        Assert.That(cut.Find("[aria-label='Category name']").HasAttribute("data-language-draft-input"), Is.True);
        Assert.That(cut.Find("[aria-label='New combo group name']").HasAttribute("data-language-draft-input"), Is.True);
        Assert.That(cut.Find("#cardCategory0").HasAttribute("data-language-draft-input"), Is.True);
        Assert.That(cut.Find("#handSize").HasAttribute("data-language-number-input"), Is.True);
        Assert.That(cut.Find("#cardCopies0").HasAttribute("data-language-number-input"), Is.True);
        Assert.That(cut.Find("#cardName0").HasAttribute("data-language-draft-input"), Is.False,
            "Card renames are accepted as typed; quick add still requires submission.");
    }

    [Test]
    public async Task RenameProtectionEndsOnlyWhenTheCategoryOrGroupEditIsFinished() {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = RenderDraftWorkspace();
        Assert.That(cut.FindAll("[data-language-edit='true']"), Is.Empty);

        await Button(cut, "Edit category A").ClickAsync(new());
        Assert.That(cut.Find("[aria-label='New name for category A']").GetAttribute("data-language-edit"), Is.EqualTo("true"));
        await cut.Find("[aria-label='New name for category A']").InputAsync(new() { Value = "" });
        Assert.That(cut.FindAll("[data-language-edit='true']").Count, Is.EqualTo(1), "Even an empty rename remains unfinished.");
        await Button(cut, "Cancel category edit").ClickAsync(new());
        Assert.That(cut.FindAll("[data-language-edit='true']"), Is.Empty);

        await Button(cut, "Edit group Group").ClickAsync(new());
        Assert.That(cut.Find("[aria-label='New name for group Group']").GetAttribute("data-language-edit"), Is.EqualTo("true"));
        await cut.Find("[aria-label='New name for group Group']").InputAsync(new() { Value = "Renamed" });
        await Button(cut, "Save group name").ClickAsync(new());
        Assert.That(cut.FindAll("[data-language-edit='true']"), Is.Empty);
        Assert.That(cut.FindComponent<ComboListEditor>().Instance.ComboGroups.Single().Name, Is.EqualTo("Renamed"));
    }

    [Test]
    public async Task OpenRuleSimulationAndImportProtectTheirDraftUntilCancelled() {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = RenderDraftWorkspace();
        await Button(cut, "Edit category A").ClickAsync(new());
        await Button(cut, "Define by properties").ClickAsync(new());
        IRenderedComponent<CategoryRuleEditor> rule = cut.FindComponent<CategoryRuleEditor>();
        Assert.That(rule.Find("[data-language-edit='true']"), Is.Not.Null);
        await Button(rule, "Cancel").ClickAsync(new());
        Assert.That(cut.FindAll("[data-language-edit='true']"), Is.Empty);

        IRenderedComponent<CardEditor> card = cut.FindComponent<CardEditor>();
        OpenSimulation(card);
        Assert.That(card.FindComponent<CardSimulationEditor>().Find("[data-language-edit='true']"), Is.Not.Null);
        await card.Find(".simulation-cancel").ClickAsync(new());
        Assert.That(cut.FindAll("[data-language-edit='true']"), Is.Empty);

        await Button(cut, "Import YDKe").ClickAsync(new());
        Assert.That(cut.Find("form[data-language-edit='true']").QuerySelector("#ydkeCodeInput"), Is.Not.Null);
        await Button(cut, "Cancel").ClickAsync(new());
        Assert.That(cut.FindAll("[data-language-edit='true']"), Is.Empty);
    }
}
