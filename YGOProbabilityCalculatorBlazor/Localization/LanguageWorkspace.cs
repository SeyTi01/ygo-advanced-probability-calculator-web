namespace YGOProbabilityCalculatorBlazor.Localization;

// One accepted calculator snapshot for a language reload. Child editors do not participate.
public sealed class LanguageWorkspace {
    public Func<string>? Capture { get; set; }
    public string? Pending { get; private set; }
    public bool Restoring { get; private set; }
    public bool Restored { get; private set; }
    public string? Failure { get; private set; }
    public event Action? Changed;

    public void Begin(string checkpoint) { Pending = checkpoint; Restoring = true; }
    public void Accept() { Pending = null; Restored = true; Changed?.Invoke(); }
    public void Reject(string message) { Failure = message; Changed?.Invoke(); }
    public void Finish() => Restoring = false;
}
