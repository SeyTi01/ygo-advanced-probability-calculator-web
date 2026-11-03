using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace YGOProbabilityCalculatorBlazor.Components.Shared;

// Owns the browser registration and receiver only. Each popup keeps its own
// open state, keyboard policy, scroll state and focus destination.
internal sealed class PopupRegistration<T>(IJSRuntime js, T receiver) : IAsyncDisposable where T : class {
    private DotNetObjectReference<T>? _reference;
    private ElementReference _panel;

    public ValueTask OpenAsync(ElementReference panel, ElementReference trigger, bool focusFirst, bool matchWidth = false) {
        Register(panel);
        return js.InvokeVoidAsync("workspaceMenus.open", panel, trigger, _reference, focusFirst, matchWidth);
    }

    public ValueTask WatchOutsideAsync(ElementReference panel, ElementReference trigger) {
        Register(panel);
        return js.InvokeVoidAsync("workspaceMenus.watchOutside", panel, trigger, _reference);
    }

    public ValueTask CloseAsync(ElementReference panel, ElementReference trigger, bool removing = false) =>
        js.InvokeVoidAsync("workspaceMenus.close", panel, trigger, removing);

    public ValueTask DetachAsync() => _reference is null ? ValueTask.CompletedTask : js.InvokeVoidAsync("workspaceMenus.dispose", _panel);

    private void Register(ElementReference panel) {
        _panel = panel;
        _reference ??= DotNetObjectReference.Create(receiver);
    }

    public async ValueTask DisposeAsync() {
        try {
            await DetachAsync();
        }
        catch (Exception exception) when (exception is JSDisconnectedException or OperationCanceledException) {
        }
        finally {
            _reference?.Dispose();
            _reference = null;
        }
    }
}
