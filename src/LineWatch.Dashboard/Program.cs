using LineWatch.Dashboard;
using LineWatch.Dashboard.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var apiBaseUrl = new Uri(builder.Configuration["ApiBaseUrl"] ?? "http://localhost:5080");

builder.Services.AddScoped(_ => new HttpClient { BaseAddress = apiBaseUrl });
builder.Services.AddScoped<LineWatchApi>();
builder.Services.AddScoped<TelemetryFeed>();

await builder.Build().RunAsync();
