using GpsTrainer.Server.Api;
using GpsTrainer.Server.Data;
using GpsTrainer.Server.Loops;
using GpsTrainer.Server.Routing;
using GpsTrainer.Server.Security;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port))
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

builder.Services.AddOpenApi();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

string connectionString;
string provider;
var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
if (builder.Environment.IsProduction())
{
    connectionString = DatabaseSetup.RequireProductionDatabaseUrl(databaseUrl);
    provider = "Postgres";
}
else if (!string.IsNullOrWhiteSpace(databaseUrl))
{
    connectionString = PostgresConnection.FromDatabaseUrl(databaseUrl);
    provider = "Postgres";
}
else
{
    connectionString = builder.Configuration.GetConnectionString("GpsTrainer")
        ?? "Data Source=data/gpstrainer.db";
    provider = builder.Configuration["GpsTrainer:DatabaseProvider"] ?? "Sqlite";
}

builder.Services.AddSingleton(sp => OpenRouteServiceEndpoints.From(sp.GetRequiredService<IConfiguration>()));
builder.Services.AddHttpClient<IOpenRouteServiceClient, OpenRouteServiceClient>((_, client) =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
    client.MaxResponseContentBufferSize = 2_000_000;
});
builder.Services.AddSingleton<SecretProtector>();
builder.Services.AddSingleton<RevokedAuthTokens>();
builder.Services.AddSingleton<ElevationLineCache>();
builder.Services.AddSingleton<AnonymousRoutingLimiter>();
builder.Services.AddSingleton<FortCollinsNetworkCatalog>();
builder.Services.AddSingleton<LoopSnapCache>();
builder.Services.AddSingleton<OrsQuotaGate>();
builder.Services.AddScoped<LoopGenerationService>();
builder.Services.AddScoped<RoutingSettingsStore>();
builder.Services.AddScoped<IRoutingProvider, OpenRouteServiceRoutingProvider>();
builder.Services.AddScoped<IRoutingProviderFactory, RoutingProviderFactory>();
builder.Services.AddScoped<OpenRouteServiceElevationProvider>();
builder.Services.AddScoped<IElevationProvider>(sp => ElevationProviderSelection.Resolve(sp));

builder.Services.AddDbContext<GpsTrainerDb>(options =>
{
    // The recovered site used SQL Server (Azure SQL and db.gpstrainer.com).
    // This port uses PostgreSQL for gps-trainer.com. SQLite stays the default
    // so tests and local runs need no server and no password.
    DatabaseSetup.UseProvider(options, provider, connectionString);
});

var app = builder.Build();

Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, "data"));

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<GpsTrainerDb>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseExtensions");
    await DatabaseExtensionsInitializer.ApplyAsync(provider, connectionString, logger);
    db.Database.EnsureCreated();
    await RoutingSchema.EnsureAsync(db);
    await RevokedTokenSchema.EnsureAsync(db);
    await scope.ServiceProvider.GetRequiredService<RevokedAuthTokens>().LoadAsync(db);
    await GpsEndpoints.SeedAsync(db, app.Configuration);
}

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseCors();
// Framework files are published under stable names (WasmFingerprintAssets=false).
// index.html is a normal static file; the publish manifest does not rewrite it.
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();
app.MapGpsApi();
app.MapGet("/api/health", (IConfiguration config) => Results.Ok(new
{
    status = "ok",
    service = "gpsTrainer",
    host = config["GpsTrainer:PublicHost"] ?? "gps-trainer.com"
}));
// The recovered ASMX service exposed ExecuteSQLCommand. That route is closed.
app.MapMethods("/api/sql", ["GET", "POST"], () => Results.NotFound());
app.MapMethods("/WebService.asmx/ExecuteSQLCommand", ["GET", "POST"], () => Results.NotFound());
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
