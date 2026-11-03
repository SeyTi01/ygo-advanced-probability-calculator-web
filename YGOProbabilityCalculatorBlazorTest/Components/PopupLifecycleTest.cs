using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using YGOProbabilityCalculatorBlazor.Components.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Components.Shared;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazorTest.Components;

public sealed class PopupLifecycleTest : CalculatorEditorTestBase {
    [Test]
    public async Task ReopeningPreservesCommandAndSelectionFocusPolicies() {
        IRenderedComponent<ActionMenu> commands = context.RenderComponent<ActionMenu>();
        await commands.Find("button").ClickAsync(new() { Detail = 0 });
        JSRuntimeInvocation commandOpen = context.JSInterop.Invocations["workspaceMenus.open"].Last();
        Assert.That(commandOpen.Arguments.Skip(3), Is.EqualTo(new object[] { true, false }));
        DotNetObjectReference<ActionMenu> commandReceiver = (DotNetObjectReference<ActionMenu>)commandOpen.Arguments[2]!;
        await commands.InvokeAsync(commandReceiver.Value.Dismiss);
        Assert.That(commands.Find(".action-menu-panel").HasAttribute("hidden"), Is.True);
        await commands.Find("button").ClickAsync(new() { Detail = 1 });
        Assert.That(context.JSInterop.Invocations["workspaceMenus.open"].Last().Arguments.Skip(3), Is.EqualTo(new object[] { false, false }));

        IRenderedComponent<RequirementPropertyPicker> choices = context.RenderComponent<RequirementPropertyPicker>();
        await choices.Find(".requirement-picker-trigger").ClickAsync(new());
        JSRuntimeInvocation choiceOpen = context.JSInterop.Invocations["workspaceMenus.open"].Last();
        Assert.That(choiceOpen.Arguments.Skip(3), Is.EqualTo(new object[] { false, true }));
        DotNetObjectReference<RequirementPropertyPicker> choiceReceiver = (DotNetObjectReference<RequirementPropertyPicker>)choiceOpen.Arguments[2]!;
        await choices.InvokeAsync(choiceReceiver.Value.Dismiss);
        Assert.That(choices.Find(".requirement-picker-panel").HasAttribute("hidden"), Is.True);
        await choices.Find(".requirement-picker-trigger").KeyDownAsync(new() { Key = "ArrowDown" });
        Assert.That(choices.Find(".requirement-picker-panel").HasAttribute("hidden"), Is.False);
        Assert.That(context.JSInterop.Invocations["workspaceMenus.open"].Last().Arguments.Skip(3), Is.EqualTo(new object[] { false, true }));
    }

    [Test]
    public async Task ChoosingANumericPropertyMovesFocusToItsValueAfterClosing() {
        IRenderedComponent<RequirementPropertyPicker> choices = context.RenderComponent<RequirementPropertyPicker>(parameters => parameters
            .Add(component => component.Value, new NumericPropertyRange(NumericPropertyKind.Level, 5, 5).Identity));
        await choices.InvokeAsync(() => choices.Instance.FocusAsync(true).AsTask());
        object? numericInput = context.JSInterop.Invocations["Blazor._internal.domWrapper.focus"].Last().Arguments[0];
        await choices.Find(".requirement-picker-trigger").ClickAsync(new());
        await choices.Find("select.requirement-options").ChangeAsync(new() { Value = "numeric:rank" });
        await choices.Find("select.requirement-options").ClickAsync(new());

        Assert.That(choices.Find(".requirement-picker-panel").HasAttribute("hidden"), Is.True);
        Assert.That(context.JSInterop.Invocations["Blazor._internal.domWrapper.focus"].Last().Arguments[0], Is.EqualTo(numericInput));
        Assert.That(choices.Find("input[id$='-number']").GetAttribute("aria-label"), Is.EqualTo("Rank"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task InterruptedBrowserCleanupStillReleasesTheManagedReceiver(bool disconnected) {
        CleanupRuntime runtime = new() {
            CleanupFailure = disconnected ? new JSDisconnectedException("Browser disconnected") : new OperationCanceledException()
        };
        object receiver = new();
        PopupRegistration<object> popup = new(runtime, receiver);
        await popup.OpenAsync(new ElementReference("panel"), new ElementReference("trigger"), false);
        DotNetObjectReference<object> reference = (DotNetObjectReference<object>)runtime.Receiver!;
        Assert.That(reference.Value, Is.SameAs(receiver));

        await popup.DisposeAsync();

        Assert.That(runtime.DisposedPanel.Id, Is.EqualTo("panel"));
        Assert.Throws<ObjectDisposedException>(() => _ = reference.Value);
        await popup.DisposeAsync();
    }

    private sealed class CleanupRuntime : IJSRuntime {
        public Exception? CleanupFailure { get; init; }
        public object? Receiver { get; private set; }
        public ElementReference DisposedPanel { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, default, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) {
            if (identifier == "workspaceMenus.open") {
                Receiver = args![2];
            }
            if (identifier == "workspaceMenus.dispose") {
                DisposedPanel = (ElementReference)args![0]!;
                return ValueTask.FromException<TValue>(CleanupFailure!);
            }

            return ValueTask.FromResult(default(TValue)!);
        }
    }
}
