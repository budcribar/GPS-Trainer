using GpsTrainer.Web;
using GpsTrainer.Web.Maps;
using GpsTrainer.Web.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var apiBase = builder.Configuration["ApiBase"];
var baseAddress = string.IsNullOrWhiteSpace(apiBase)
    ? new Uri(builder.HostEnvironment.BaseAddress)
    : new Uri(apiBase);
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = baseAddress });
builder.Services.AddScoped<Session>();
builder.Services.AddScoped<GpsApi>();
builder.Services.AddScoped<ICourseMap>(CourseMaps.Create);

await builder.Build().RunAsync();
