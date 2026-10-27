using System.Globalization;
using System.Text.Json;

namespace YGOProbabilityCalculatorBlazor.Localization;

public sealed record ProductionLocale(string Id, string Name);

// Website preference, never SessionState. Browser and .NET use the same registry.
public sealed class LocalePreference {
    public static IReadOnlyList<ProductionLocale> Available { get; } = ReadRegistry();
    public static IReadOnlyList<string> SupportedLocales { get; } = Available.Select(locale => locale.Id).ToArray();
    public string Locale { get; private set; } = "en";

    public CultureInfo? NumericCulture { get; set; }

    public void RestoreNumericCulture() {
        if (NumericCulture is not { } numeric) { return; }
        CultureInfo.DefaultThreadCurrentCulture = numeric;
        CultureInfo.CurrentCulture = numeric;
    }

    private static ProductionLocale[] ReadRegistry() {
        using Stream stream = typeof(LocalePreference).Assembly.GetManifestResourceStream("ProductionLocales.json")!;
        return JsonSerializer.Deserialize<ProductionLocale[]>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    public static string Resolve(string? explicitPreference = null, IEnumerable<string>? browserPreferences = null) {
        IEnumerable<string?> candidates = new[] { explicitPreference }.Concat(browserPreferences ?? []);
        foreach (string? candidate in candidates) {
            if (string.IsNullOrWhiteSpace(candidate)) { continue; }
            string normalized = candidate.Trim().ToLowerInvariant();
            while (normalized.Length > 0) {
                string? match = SupportedLocales.FirstOrDefault(locale => locale.Equals(normalized, StringComparison.OrdinalIgnoreCase));
                if (match is not null) { return match; }
                int separator = normalized.LastIndexOf('-');
                normalized = separator < 0 ? "" : normalized[..separator];
            }
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
