using System.Text.Json;
using System.Text.Json.Serialization;
using YGOProbabilityCalculatorBlazor.Services.Session;
using YGOProbabilityCalculatorBlazor.Services.Converter;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Localization;

// Only live editor state for one language reload; never part of the session schema.
public sealed class LanguageWorkspace {
    private readonly Dictionary<string, (object Owner, Func<string> Capture)> _capture = new(StringComparer.Ordinal);
    private Dictionary<string, string> _pending = new(StringComparer.Ordinal);
    public bool Restoring { get; private set; }
    public bool Complete => Restoring && Failure is null && _pending.Count == 0;
    public string? Failure { get; private set; }
    public Func<bool>? IsBusy { get; set; }
    public event Action? Changed;

    public void Begin(Dictionary<string, string> states) {
        _pending = new(states, StringComparer.Ordinal);
        Failure = null;
        Restoring = true;
        if (!_pending.Remove("$build", out string? build) || build != BuildStamp) {
            Failure = UiText.Get("Language.CaptureFailed");
        }
    }

    public void Finish() => Restoring = false;

    public bool Register(string key, object owner, Func<string> capture, Action<string> restore) {
        _capture[key] = (owner, capture);
        if (Failure is not null) { return false; }
        if (!_pending.TryGetValue(key, out string? state)) { return false; }
        try {
            restore(state);
            _pending.Remove(key);
        }
        catch (Exception ex) { Failure = ex.Message; }
        Changed?.Invoke();
        return true;
    }

    public void Remove(string key, object owner) {
        if (_capture.TryGetValue(key, out (object Owner, Func<string> Capture) entry) && ReferenceEquals(entry.Owner, owner)) { _capture.Remove(key); }
    }

    public Dictionary<string, string> Capture() {
        if (Restoring) { throw new InvalidOperationException(UiText.Get("Language.RestorationPending")); }
        Dictionary<string, string> states = _capture.ToDictionary(entry => entry.Key, entry => entry.Value.Capture(), StringComparer.Ordinal);
        states.Add("$build", BuildStamp);
        return states;
    }

    private static string BuildStamp => typeof(LanguageWorkspace).Assembly.ManifestModule.ModuleVersionId.ToString("D");
}

public static class LanguageDraftState {
    private static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions() {
        JsonSerializerOptions options = SessionService.CreateSerializerOptions();
        options.IncludeFields = true;
        options.Converters.Add(new NullableSelectorConverter());
        return options;
    }

    // Round-trip each checkpoint before navigating; unsupported shapes fail in the live app.
    public static string Pack<T>(T state) {
        string json = JsonSerializer.Serialize(state, Options);
        T restored = Unpack<T>(json);
        if (JsonSerializer.Serialize(restored, Options) != json) {
            throw new InvalidOperationException(UiText.Get("Language.CaptureFailed"));
        }
        return json;
    }

    public static T Unpack<T>(string json) => JsonSerializer.Deserialize<T>(json, Options)!;

    // Editor cursors/seeds can be absent. Session selectors retain their stricter converter.
    private sealed class NullableSelectorConverter : JsonConverter<CardSelector> {
        private readonly CardSelectorConverter _inner = new();
        public override CardSelector Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
            _inner.Read(ref reader, type, options);
        public override void Write(Utf8JsonWriter writer, CardSelector value, JsonSerializerOptions options) =>
            _inner.Write(writer, value, options);
    }

}
