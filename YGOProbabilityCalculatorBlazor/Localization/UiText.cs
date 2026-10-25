using System.Diagnostics;
using System.Globalization;
using System.Resources;
using System.Text;

namespace YGOProbabilityCalculatorBlazor.Localization;

// ResourceManager provides the same satellite/parent/neutral fallback used by
// .NET's string localizers, including for static presentation formatters.
public static class UiText {
    private static readonly AsyncLocal<ResourceManager?> ResourceOverride = new();
    private static readonly ResourceManager CanonicalResources = new(
        "YGOProbabilityCalculatorBlazor.Localization.Strings", typeof(UiText).Assembly);

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
        if (!HasSameArguments(english, localized)) {
            Trace.TraceWarning("Invalid localization placeholders for resource {0} ({1}).", key, locale.Name);
            return NormalizeLines(english);
        }

        return NormalizeLines(localized);
    }

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
