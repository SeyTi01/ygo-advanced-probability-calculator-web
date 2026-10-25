using System.Globalization;

namespace YGOProbabilityCalculatorBlazor.Localization;

// This preference belongs to the application shell, never to SessionState.
// Supply an explicit shell preference and navigator.languages here when another
// production locale is added. The same resolver runs during static prerendering.
public sealed class LocalePreference {
    public static IReadOnlyList<string> SupportedLocales { get; } = Array.AsReadOnly(new[] { "en" });
    public string Locale { get; private set; } = "en";

    public static string Resolve(string? explicitPreference = null, IEnumerable<string>? browserPreferences = null) {
        IEnumerable<string?> candidates = new[] { explicitPreference }.Concat(browserPreferences ?? []);
        foreach (string? candidate in candidates) {
            if (string.IsNullOrWhiteSpace(candidate)) { continue; }
            string? match = SupportedLocales.FirstOrDefault(locale =>
                string.Equals(locale, candidate, StringComparison.OrdinalIgnoreCase)
                || candidate.StartsWith(locale + "-", StringComparison.OrdinalIgnoreCase));
            if (match is not null) { return match; }
        }
        return "en";
    }

    public void Initialize(string? explicitPreference = null, IEnumerable<string>? browserPreferences = null) {
        Locale = Resolve(explicitPreference, browserPreferences);
        CultureInfo culture = CultureInfo.GetCultureInfo(Locale);
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }
}
