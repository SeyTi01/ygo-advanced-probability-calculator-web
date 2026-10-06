using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using YGOProbabilityCalculatorBlazor.Services.DeckImport;
using YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.ProbabilityCalculator;
using YGOProbabilityCalculatorBlazor.Services.Session;
using YGOProbabilityCalculatorBlazor.Services.Shared;

namespace YGOProbabilityCalculatorBlazor;

public static class Program {
    public static async Task Main(string[] args) {
        var builder = WebAssemblyHostBuilder.CreateDefault(args);
        builder.RootComponents.Add<App>("#app");
        builder.RootComponents.Add<HeadOutlet>("head::after");

        ConfigureServices(builder.Services, builder.HostEnvironment.BaseAddress);

        await builder.Build().RunAsync();
    }

    // The publish-time prerenderer calls this hook too; keep runtime registrations shared.
    public static void ConfigureServices(IServiceCollection services, string baseAddress) {
        services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(baseAddress) });
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
        services.AddScoped<IBackgroundCalculator, BackgroundCalculator>();
    }
}
