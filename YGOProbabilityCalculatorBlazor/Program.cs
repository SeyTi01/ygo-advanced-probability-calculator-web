using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using YGOProbabilityCalculatorBlazor.Services.DeckImport;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.Session;
using YGOProbabilityCalculatorBlazor.Services.Shared;
using YGOProbabilityCalculatorBlazor.Localization;
using Microsoft.JSInterop;
using System.Globalization;

namespace YGOProbabilityCalculatorBlazor;

public static class Program {
    public static async Task Main(string[] args) {
        WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(args);
        builder.RootComponents.Add<App>("#app");
        builder.RootComponents.Add<HeadOutlet>("head::after");

        ConfigureServices(builder.Services, builder.HostEnvironment.BaseAddress);

        WebAssemblyHost host = builder.Build();
        IJSRuntime js = host.Services.GetRequiredService<IJSRuntime>();
        StartupLocale startup = await js.InvokeAsync<StartupLocale>("ygoLocale.getStartup");
        // Keep both cultures on applicationCulture until RunAsync has loaded satellites.
        CultureInfo numeric;
        try { numeric = CultureInfo.GetCultureInfo(startup.NumericCulture); }
        catch (CultureNotFoundException) { numeric = CultureInfo.GetCultureInfo("en-US"); }
        LocalePreference preference = host.Services.GetRequiredService<LocalePreference>();
        preference.NumericCulture = numeric;
        preference.Initialize(startup.Locale);
        await host.RunAsync();
    }

    public sealed record StartupLocale(string Locale, string NumericCulture);

    // The publish-time prerenderer calls this hook too; keep runtime registrations shared.
    public static void ConfigureServices(IServiceCollection services, string baseAddress) {
        LocalePreference locale = new();
        locale.Initialize();
        services.AddScoped<LocalePreference>();
        services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(baseAddress) });
        services.AddScoped<IDeckImportService, DeckImportService>();
        services.AddScoped<ISessionService, SessionService>();
        services.AddScoped<ILegacyCardMetadataEnricher, LegacyCardMetadataEnricher>();
        services.AddScoped<ICardInfoService, CardInfoService>();
        services.AddScoped<ICardArtworkService, CardArtworkService>();
        services.AddScoped<IFileService, FileService>();
        services.AddScoped<ISerializer, JsonSerializer>();
        services.AddScoped<IPendingSessionService, PendingSessionService>();
        services.AddScoped<ILocalStorageService, LocalStorageService>();
        services.AddScoped<IProbabilityCalculatorService, ProbabilityCalculatorService>();
        services.AddTransient<IBackgroundCalculator, BackgroundCalculator>();
    }
}
