using AngleSharp.Dom;
using Bunit;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;

namespace YGOProbabilityCalculatorBlazorTest.Components;

internal static class WorkspaceActions {
    public static IElement RowAction(IRenderedFragment fragment, string rowLabel, string command) {
        IElement trigger = fragment.Find($"button[aria-label='Actions for {rowLabel}']");
        if (trigger.GetAttribute("aria-expanded") != "true") {
            trigger.Click();
        }

        return fragment.Find($"#{trigger.GetAttribute("aria-controls")}").QuerySelectorAll("button")
            .Single(button => button.TextContent.Trim() == command);
    }

    public static IElement FirstRowAction(IRenderedFragment fragment, string kind, string command) {
        string label = fragment.Find($"button[aria-label^='Actions for {kind} ']").GetAttribute("aria-label")!;

        return RowAction(fragment, label[12..], command);
    }

    public static async Task SetActiveAsync(IRenderedFragment fragment, string selector, bool active) {
        IElement command = fragment.Find(selector);
        Assert.That(command.TextContent.Trim(), Is.EqualTo(active ? "Activate" : "Deactivate"));
        IElement trigger = command.Closest(".action-menu")!.QuerySelector(".action-menu-trigger")!;
        if (trigger.GetAttribute("aria-expanded") != "true") {
            await trigger.ClickAsync(new());
        }
        await fragment.Find(selector).ClickAsync(new());
    }

    public static async Task AddManualCardAsync(IRenderedFragment fragment) {
        await fragment.Find("#quickCardName").InputAsync(new() { Value = "Manual card" });
        await fragment.Find("#quickCardName").KeyDownAsync(new() { Key = "Enter" });
        IRenderedComponent<CardEditor> editor = fragment.FindComponents<CardEditor>().Last();
        await editor.Find(".accordion-button").ClickAsync(new());
        await editor.Find("input[id^='cardName']").InputAsync(new() { Value = "" });
    }
}
