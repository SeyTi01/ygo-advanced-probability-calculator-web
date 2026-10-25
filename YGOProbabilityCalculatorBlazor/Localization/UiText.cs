using System.Diagnostics;
using System.Globalization;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Text;

namespace YGOProbabilityCalculatorBlazor.Localization;

// ResourceManager provides the same satellite/parent/neutral fallback used by
// .NET's string localizers, including for static presentation formatters.
public static class UiText {
    private static readonly AsyncLocal<ResourceManager?> ResourceOverride = new();
    private static readonly ResourceManager CanonicalResources = new(
        "YGOProbabilityCalculatorBlazor.Localization.Strings", typeof(UiText).Assembly);
    private static readonly ConditionalWeakTable<ResourceManager, TranslationCache> ValidatedTranslations = new();

    public static ResourceManager Resources => ResourceOverride.Value ?? CanonicalResources;

    public static string English(string key) => NormalizeLines(CanonicalResources.GetString(key, CultureInfo.InvariantCulture)
        ?? Missing(key));

    // Test satellites live in the test assembly and cannot ship in the app.
    // AsyncLocal keeps their resource source isolated from concurrent renders.
    internal static IDisposable UseResources(ResourceManager resources) {
        ResourceManager? previous = ResourceOverride.Value;
        ResourceOverride.Value = resources;
        return new ResourceScope(previous);
    }

    private sealed class ResourceScope(ResourceManager? previous) : IDisposable {
        public void Dispose() => ResourceOverride.Value = previous;
    }

    public static string Get(string key) => Get(Resources, key, CultureInfo.CurrentUICulture);

    public static string Get(ResourceManager resources, string key, CultureInfo locale) {
        string? english = resources.GetString(key, CultureInfo.InvariantCulture);
        if (english is null) {
            return Missing(key);
        }

        string localized = resources.GetString(key, locale) ?? english;
        if (string.Equals(english, localized, StringComparison.Ordinal)) {
            return NormalizeLines(english);
        }

        return ValidatedTranslations.GetValue(resources, static _ => new()).Get(key, locale.Name, english, localized);
    }

    // Resource sources have independent lifetimes. Bound each source's locale/key
    // cache; eviction only repeats validation, and changed text is never reused.
    private sealed class TranslationCache {
        private const int Capacity = 1024;
        private readonly Dictionary<(string Key, string Locale), ValidatedTranslation> _entries = [];

        public string Get(string key, string locale, string english, string localized) {
            lock (_entries) {
                if (_entries.TryGetValue((key, locale), out ValidatedTranslation cached)
                    && cached.English == english && cached.Localized == localized) {
                    return cached.Text;
                }

                bool valid = HasSameArguments(english, localized);
                if (!valid) {
                    Trace.TraceWarning("Invalid localization placeholders for resource {0} ({1}).", key, locale);
                }
                string text = NormalizeLines(valid ? localized : english);
                if (_entries.Count >= Capacity) { _entries.Clear(); }
                _entries[(key, locale)] = new(english, localized, text);

                return text;
            }
        }
    }

    private readonly record struct ValidatedTranslation(string English, string Localized, string Text);

    // CurrentCulture remains the caller's numeric formatting contract. Locale
    // selection changes only CurrentUICulture, never mathematical/JSON inputs.
    public static string Format(string key, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), arguments);

    public static bool HasSameArguments(string english, string localized) {
        try {
            CompositeFormat source = CompositeFormat.Parse(english);
            CompositeFormat translation = CompositeFormat.Parse(localized);
            return source.MinimumArgumentCount == translation.MinimumArgumentCount
                && ArgumentIndices(english).SetEquals(ArgumentIndices(localized));
        }
        catch (FormatException) {
            return false;
        }
    }

    private static HashSet<int> ArgumentIndices(string format) {
        HashSet<int> indices = [];
        for (int index = 0; index < format.Length; index++) {
            if (format[index] != '{') { continue; }
            if (index + 1 < format.Length && format[index + 1] == '{') { index++; continue; }
            int start = ++index;
            while (index < format.Length && char.IsAsciiDigit(format[index])) { index++; }
            indices.Add(int.Parse(format.AsSpan(start, index - start), CultureInfo.InvariantCulture));
        }
        return indices;
    }

    private static string Missing(string key) {
        Trace.TraceWarning("Missing English localization resource: {0}.", key);
        return key;
    }

    // ResX readers on Windows can expand literal newlines. Copy/export messages
    // have an existing LF contract, independent of the publishing host.
    private static string NormalizeLines(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);
}
