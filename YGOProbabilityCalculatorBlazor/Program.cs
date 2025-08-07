using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using YGOProbabilityCalculatorBlazor.Services.DeckImport;
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

        builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
        builder.Services.AddScoped<IDeckImportService, DeckImportService>();
        builder.Services.AddScoped<ISessionService, SessionService>();
        builder.Services.AddScoped<ICardInfoService, CardInfoService>();
        builder.Services.AddScoped<IFileService, FileService>();
        builder.Services.AddScoped<ISerializer, JsonSerializer>();
        builder.Services.AddScoped<IPendingSessionService, PendingSessionService>();
        builder.Services.AddScoped<ILocalStorageService, LocalStorageService>();
        builder.Services.AddScoped<IProbabilityCalculatorService, ProbabilityCalculatorService>();

        await builder.Build().RunAsync();
    }
}
