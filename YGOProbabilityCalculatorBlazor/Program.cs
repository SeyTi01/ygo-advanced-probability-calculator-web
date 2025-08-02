using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using YGOProbabilityCalculatorBlazor;
using YGOProbabilityCalculatorBlazor.Services.DeckImport;
using YGOProbabilityCalculatorBlazor.Services.Interface;
using YGOProbabilityCalculatorBlazor.Services.Session;
using YGOProbabilityCalculatorBlazor.Services.Shared;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<DeckImportService>();
builder.Services.AddScoped<ISessionService, SessionService>();
builder.Services.AddScoped<ICardInfoService, CardInfoService>();
builder.Services.AddScoped<IFileService, FileService>();
builder.Services.AddScoped<ISerializer, JsonSerializer>();

await builder.Build().RunAsync();
