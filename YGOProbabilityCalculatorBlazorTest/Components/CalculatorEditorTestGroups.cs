using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Moq;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Models;
using YGOProbabilityCalculatorBlazor.Services.DeckImport;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.Session;
using YGOProbabilityCalculatorBlazor.Services.Shared;
using YGOProbabilityCalculatorBlazorTest.Services.ProbabilityCalculator;
using TestContext = Bunit.TestContext;

namespace YGOProbabilityCalculatorBlazorTest.Components;

[TestFixture]
public sealed class CalculatorEditorTestGroups : CalculatorEditorTestBase {

    [Test]
    public void ComboHeaderShowsOnlyValidGroupMembershipAsSeparateMetadata() {
        Card card = new([a], 2, "Starter", id: "starter-card");
        const string groupId = "full-combo";
        const string staleGroupId = "removed-group";
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(new SessionState {
            Categories = [a],
            Cards = [card],
            Combos = [
                new([new ComboCategory(a, 1, 1)], "Grouped combo", active: false, groupId: groupId,
                    cards: [new ComboCard(card.Id, 1, 1)]),
                new([], "No group assigned"),
                new([], "Stale group reference", groupId: staleGroupId)
            ],
            ComboGroups = [new(groupId, "Full Combo"), new("optional-route", "Optional Route")],
            HandSize = 2
        });
        IReadOnlyList<IRenderedComponent<ComboEditor>> editors = cut.FindComponents<ComboEditor>();
        IElement groupedHeader = editors[0].Find(".combo-header-content");
        IElement? membership = groupedHeader.QuerySelector(".combo-group-membership");

        Assert.That(membership, Is.Not.Null);
        Assert.That(membership!.TextContent.Trim(), Is.EqualTo("Full Combo"));
        Assert.That(membership.TextContent, Does.Not.Contain("Group:"));
        Assert.That(membership.GetAttribute("aria-label"), Is.EqualTo("Group: Full Combo"));
        Assert.That(membership.QuerySelector("svg")?.GetAttribute("aria-hidden"), Is.EqualTo("true"));
        Assert.That(membership.QuerySelector("svg")?.GetAttribute("focusable"), Is.EqualTo("false"));
        Assert.That(membership.ClassList, Does.Not.Contain("category-tag"));
        Assert.That(membership.ClassList, Does.Not.Contain("card-property-tag"));
        Assert.That(membership.ClassList, Does.Not.Contain("combo-card-tag"));
        Assert.That(groupedHeader.QuerySelector(".category-tag")?.TextContent, Does.Contain("A (1)"));
        Assert.That(groupedHeader.QuerySelector(".combo-card-tag")?.TextContent, Does.Contain("Card: Starter (1)"));
        Assert.That(groupedHeader.QuerySelector(".badge.text-bg-secondary")?.TextContent, Is.EqualTo("Inactive"));
        Assert.That(Array.IndexOf(groupedHeader.Children.ToArray(), membership),
            Is.LessThan(Array.IndexOf(groupedHeader.Children.ToArray(), groupedHeader.QuerySelector(".expression-term"))));

        foreach (IRenderedComponent<ComboEditor> ungroupedEditor in editors.Skip(1)) {
            IElement headerContent = ungroupedEditor.Find(".combo-header-content");
            Assert.That(headerContent.QuerySelector(".combo-group-membership"), Is.Null);
            Assert.That(headerContent.TextContent, Does.Not.Contain("Ungrouped"));
            Assert.That(headerContent.TextContent, Does.Not.Contain(staleGroupId));
        }
    }

    [Test]
    public void ComboGroupsUseCategoryStyleInlineEditingAndKeepAssignmentsAndComboDrafts() {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(Session());
        IRenderedComponent<ComboEditor> first = cut.FindComponents<ComboEditor>()[0];
        Assert.That(first.Find(".combo-header-content").QuerySelector(".combo-group-membership"), Is.Null);
        first.Find("#comboCategory0").Change("user:B");
        first.Find("#minCount0").Input("0");
        first.Find("#maxCount0").Input("0");

        cut.Find("[aria-label='Add combo group']").Click();
        Assert.That(cut.Find("[role=alert]").TextContent, Does.Contain("cannot be empty"));
        cut.Find("[aria-label='New combo group name']").Input("Tier 1");
        cut.Find("[aria-label='New combo group name']")
            .KeyUp(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });
        Assert.That(cut.FindAll(".combo-group-chip"), Has.Count.EqualTo(1));
        cut.Find("[aria-label='New combo group name']").Input("tier 1");
        cut.Find("[aria-label='New combo group name']")
            .KeyUp(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });
        Assert.That(cut.Find("[role=alert]").TextContent, Does.Contain("already exists"));
        string groupId = first.Find("#comboGroup0 option:not([value=''])").GetAttribute("value")!;
        first.Find("#comboGroup0").Change(groupId);
        Assert.That(first.Find(".combo-header-content .combo-group-membership").TextContent,
            Does.Contain("Tier 1"));
        Assert.That(first.Find("#maxCount0").GetAttribute("value"), Is.EqualTo("0"));

        cut.Find("[aria-label='New combo group name']").Input("Tier 2");
        cut.Find("[aria-label='New combo group name']")
            .KeyUp(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });
        string secondId = cut.FindAll("#comboGroup0 option:not([value=''])").ToArray()[1].GetAttribute("value")!;

        cut.Find("[aria-label='Edit group Tier 1']").Click();
        context.JSInterop.VerifyInvoke("Blazor._internal.domWrapper.focus", 1);
        cut.Find("[aria-label='New name for group Tier 1']").Input("");
        cut.Find("[aria-label='Save group name']").Click();
        Assert.That(cut.Find("[role=alert]").TextContent, Does.Contain("cannot be empty"));
        cut.Find("[aria-label='New name for group Tier 1']").Input("tier 2");
        cut.Find("[aria-label='Save group name']").Click();
        Assert.That(cut.Find("[role=alert]").TextContent, Does.Contain("already exists"));
        cut.Find("[aria-label='New name for group Tier 1']").Input("Tier One");
        cut.Find("[aria-label='New name for group Tier 1']")
            .KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });
        context.JSInterop.VerifyInvoke("Blazor._internal.domWrapper.focus", 2);
        Assert.That(cut.Find("[aria-label='Edit group Tier One']").TextContent, Is.EqualTo("Tier One"));
        Assert.That(first.Instance.Combo.GroupId,
            Is.EqualTo(groupId));
        Assert.That(first.Find(".combo-header-content .combo-group-membership").TextContent,
            Does.Contain("Tier One"));
        Assert.That(cut.Find("#comboGroup0").TextContent, Does.Contain("Tier One"));
        Assert.That(first.Find("#maxCount0").GetAttribute("value"), Is.EqualTo("0"));

        cut.Find("[aria-label='Edit group Tier 2']").Click();
        IElement secondRename = cut.Find("[aria-label='New name for group Tier 2']");
        secondRename.Input("Discarded");
        secondRename.KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });
        context.JSInterop.VerifyInvoke("Blazor._internal.domWrapper.focus", 4);
        Assert.That(cut.Find("[aria-label='Edit group Tier 2']").TextContent, Is.EqualTo("Tier 2"));

        cut.Find("#comboGroup0").Change(secondId);
        Assert.That(first.Instance.Combo.GroupId, Is.EqualTo(secondId));
        Assert.That(first.Find(".combo-header-content .combo-group-membership").TextContent,
            Does.Contain("Tier 2"));
        Assert.That(first.Find("#maxCount0").GetAttribute("value"), Is.EqualTo("0"));

        cut.Find("#comboGroup0").Change("");
        Assert.That(first.Instance.Combo.GroupId, Is.Null);
        Assert.That(first.Find(".combo-header-content").QuerySelector(".combo-group-membership"), Is.Null);
        cut.Find("#comboGroup0").Change(groupId);
        Assert.That(first.Find(".combo-header-content .combo-group-membership").TextContent,
            Does.Contain("Tier One"));

        cut.Find("[aria-label='Remove group Tier One']").Click();
        Assert.That(cut.FindAll(".combo-group-chip"), Has.Count.EqualTo(1));
        Assert.That(first.Instance.Combo.GroupId, Is.Null);
        Assert.That(first.Find(".combo-header-content").QuerySelector(".combo-group-membership"), Is.Null);
        cut.Find("[aria-label='Remove group Tier 2']").Click();
        Assert.That(cut.FindAll(".combo-group-chip"), Is.Empty);
        Assert.That(first.Instance.Combo.GroupId, Is.Null);
        Assert.That(first.Find("#maxCount0").GetAttribute("value"), Is.EqualTo("0"));
        Assert.That(cut.FindComponents<ComboEditor>(), Has.Count.EqualTo(2));
    }

    [Test]
    public async Task GroupResultsUseActiveMembersAndAllGroupChangesInvalidateResults() {
        SessionState session = Session();
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(session);
        await cut.Find("[aria-label='New combo group name']").InputAsync(new() { Value = "Tier 1" });
        await cut.Find("[aria-label='Add combo group']").ClickAsync(new());
        string groupId = cut.Find("#comboGroup0 option:not([value=''])").GetAttribute("value")!;
        await cut.Find("#comboGroup0").ChangeAsync(new() { Value = groupId });
        await cut.Find("#comboGroup1").ChangeAsync(new() { Value = groupId });
        IElement groupChip = cut.Find(".combo-group-chip");
        Assert.That(groupChip.ClassList.Contains("me-2"), Is.True);
        Assert.That(groupChip.QuerySelector(".category-name-trigger")?.TextContent.Trim(), Is.EqualTo("Tier 1"));

        await RenameGroupWithEnter(cut, "Tier 1", "Tier One");
        await Button(cut, "Calculate").ClickAsync(new());
        {
            IElement result = cut.Find(".probability-results");
            List<Combo> active = cut.FindComponents<ComboEditor>().Select(editor => editor.Instance.Combo).ToList();
            double expected = SmallDeckOracle.EnumerateProbability(session.Cards, active, 2);
            Assert.That(result.QuerySelector(".probability-group .combo-probability-value")!.TextContent,
                Is.EqualTo(expected.ToString("P2")));
            Assert.That(result.QuerySelectorAll(".combo-probability-item").Length, Is.EqualTo(2));
            Assert.That(result.QuerySelector(".probability-group")!.TextContent, Does.Contain("Tier One"));
            Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.False);
        }

        IRenderedComponent<ComboEditor> secondComboEditor = cut.FindComponents<ComboEditor>()[1];
        await secondComboEditor.Find("#comboActive1").ChangeAsync(new() { Value = false });
        Assert.That(secondComboEditor.Find("#comboActive1").HasAttribute("checked"), Is.False);
        Assert.That(secondComboEditor.Instance.Combo.Active, Is.False);
        AssertPreviousResult(cut);
        await Button(cut, "Calculate").ClickAsync(new());
        {
            Combo first = cut.FindComponents<ComboEditor>()[0].Instance.Combo;
            double expected = SmallDeckOracle.EnumerateProbability(session.Cards, [first], 2);
            Assert.That(cut.Find(".probability-group .combo-probability-value").TextContent,
                Is.EqualTo(expected.ToString("P2")));
            Assert.That(cut.FindAll(".combo-probability-item"), Has.Count.EqualTo(1));
            Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.False);
        }
        await cut.Find("#comboGroup0").ChangeAsync(new() { Value = "" });
        AssertPreviousResult(cut);
        await Button(cut, "Calculate").ClickAsync(new());
        Assert.That(
            cut.Find(".probability-group .combo-probability-value").TextContent, Is.EqualTo(0.0.ToString("P2")));
        Assert.That(Button(cut, "Calculate").HasAttribute("disabled"), Is.False);
    }

    [Test]
    public void GroupMembershipAndInactiveStateRoundTripWhileLegacySessionsRemainUngrouped() {
        IRenderedComponent<ProbabilityCalculatorComponent> cut = Render(Session());
        cut.Find("[aria-label='New combo group name']").Input("Tier 1");
        cut.Find("[aria-label='Add combo group']").Click();
        string groupId = cut.Find("#comboGroup0 option:not([value=''])").GetAttribute("value")!;
        cut.Find("#comboGroup0").Change(groupId);
        cut.Find("#comboActive0").Change(false);
        Button(cut, "Save Session").Click();
        JSRuntimeInvocation invocation = context.JSInterop.Invocations["saveSessionFile"].Single();
        string json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String((string)invocation.Arguments[1]!));
        Assert.That(json, Does.Contain("\"ComboGroups\"").And.Contain("\"GroupId\""));

        cut.FindComponents<InputFile>()[1].UploadFiles(InputFileContent.CreateFromText(json, "groups.json"));
        cut.WaitForAssertion(() => {
            Assert.That(cut.FindAll(".combo-group-chip"), Has.Count.EqualTo(1));
            Assert.That(cut.FindComponents<ComboEditor>()[0].Instance.Combo.GroupId, Is.EqualTo(groupId));
            Assert.That(cut.FindComponents<ComboEditor>()[0].Instance.Combo.Active, Is.False);
        });

        const string legacy = """
            {"Categories":[{"Name":"A"}],"Cards":[],"Combos":[{"Categories":[],"Name":"Legacy"}],"HandSize":5}
            """;
        cut.FindComponents<InputFile>()[1].UploadFiles(InputFileContent.CreateFromText(legacy, "legacy.json"));
        cut.WaitForAssertion(() => {
            Assert.That(cut.FindAll(".combo-group-chip"), Is.Empty);
            Assert.That(cut.FindComponent<ComboEditor>().Instance.Combo.GroupId, Is.Null);
            Assert.That(cut.FindComponent<ComboEditor>().Instance.Combo.Active, Is.True);
        });
    }

}
