using System.Globalization;
using System.Resources;
using YGOProbabilityCalculatorBlazor.Localization;

namespace YGOProbabilityCalculatorBlazorTest.Localization;

internal sealed class PresentationLocale : IDisposable {
    internal static ResourceManager Resources { get; } = new(
        "YGOProbabilityCalculatorBlazorTest.Localization.Strings", typeof(PresentationLocale).Assembly);
    private readonly CultureInfo _previous = CultureInfo.CurrentUICulture;
    private readonly IDisposable? _scope;

    internal PresentationLocale(string locale = "fr", bool production = false) {
        _scope = production ? null : UiText.UseResources(Resources);
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(locale);
    }

    public void Dispose() {
        CultureInfo.CurrentUICulture = _previous;
        _scope?.Dispose();
    }
}
